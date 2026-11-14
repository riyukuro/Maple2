using System;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A timed item is past its time from the second of its expiry on, as the client counts it (CItem::IsExpired).
public class ItemExpiryTests {
    [Test]
    public void AnItemIsExpiredFromItsExpirySecondOn() {
        var test = new TestGameSession();
        Item item = test.Store(TestGameSession.Metadata(20000001, type: 2));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.Multiple(() => {
            item.ExpiryTime = 0;
            Assert.That(item.IsExpired(), Is.False);
            item.ExpiryTime = now + 60;
            Assert.That(item.IsExpired(), Is.False);
            item.ExpiryTime = now;
            Assert.That(item.IsExpired(), Is.True);
            Assert.That(SetItemRules.Expired(now, now, false), Is.True);
        });
    }

    // The online check counts the worn items past their time; the totals are rebuilt when the count is not the
    // one they were built with.
    [Test]
    public void AWornItemRunningOut_ChangesTheCount() {
        var test = new TestGameSession();
        Item gear = test.Store(TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1));
        Item outfit = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, type: 1));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        gear.ExpiryTime = now + 10;

        Assert.Multiple(() => {
            Assert.That(StatsManager.ExpiredWorn([gear, outfit], now), Is.Zero);
            Assert.That(StatsManager.ExpiredWorn([gear, outfit], now + 9), Is.Zero);
            Assert.That(StatsManager.ExpiredWorn([gear, outfit], now + 10), Is.EqualTo(1));
        });
    }

    // A summoned pet past its time takes nothing into its bag (the client does not ask, petmanager.cpp:533) and
    // the item stays in the character's bag; before its time the bag's own answer comes (here: no slots).
    [Test]
    public void AnExpiredPet_TakesNothing() {
        const int petId = 60000001;
        var test = new TestGameSession();
        ItemMetadata petData = TestGameSession.Metadata(petId, type: 11, slotMax: 1);
        Item pet = test.Store(petData with { Property = petData.Property with { PetId = petId } });
        Item potion = test.Store(TestGameSession.Metadata(20000001, type: 2), amount: 3);
        GameSession session = test.Start();
        Item summoned = session.Item.Inventory.Get(pet.Uid)!;
        // Add reads the session, the summoned item and the pet's bag only; the field pet itself needs the npc and
        // the navigation data a test session has none of.
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var fieldPet = (FieldPet) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        typeof(FieldPet).GetField(nameof(FieldPet.Pet), any)!.SetValue(fieldPet, summoned);
        var manager = (PetManager) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
        typeof(PetManager).GetField("session", any)!.SetValue(manager, session);
        typeof(PetManager).GetField("pet", any)!.SetValue(manager, fieldPet);
        typeof(PetManager).GetField("items", any)!.SetValue(manager, new Maple2.Server.Game.Manager.Items.ItemCollection(0));
        session.Pet = manager;

        Assert.That(session.Pet.Add(potion.Uid, -1, 1), Is.EqualTo(StringCode.s_pet_inventory_not_sendin));
        summoned.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1;
        Assert.Multiple(() => {
            Assert.That(session.Pet.Add(potion.Uid, -1, 1), Is.EqualTo(StringCode.s_empty_string));
            Assert.That(session.Item.Inventory.Get(potion.Uid)?.Amount, Is.EqualTo(3));
        });
    }

    // The bag takes a count from the client of 1 up to the stack (StorageRules.IsCount, as for storage). A zero
    // or a negative count is refused and the stack stays; the inventory's Remove took a negative one as the
    // whole stack.
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(4)]
    public void APetBag_RefusesABadCount(int amount) {
        const int petId = 60000001;
        var test = new TestGameSession();
        ItemMetadata petData = TestGameSession.Metadata(petId, type: 11, slotMax: 1);
        Item pet = test.Store(petData with { Property = petData.Property with { PetId = petId } });
        Item potion = test.Store(TestGameSession.Metadata(20000001, type: 2), amount: 3);
        GameSession session = test.Start();
        Item summoned = session.Item.Inventory.Get(pet.Uid)!;
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var fieldPet = (FieldPet) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        typeof(FieldPet).GetField(nameof(FieldPet.Pet), any)!.SetValue(fieldPet, summoned);
        var manager = (PetManager) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
        typeof(PetManager).GetField("session", any)!.SetValue(manager, session);
        typeof(PetManager).GetField("pet", any)!.SetValue(manager, fieldPet);
        var bag = new Maple2.Server.Game.Manager.Items.ItemCollection(2);
        typeof(PetManager).GetField("items", any)!.SetValue(manager, bag);
        session.Pet = manager;

        Assert.Multiple(() => {
            Assert.That(session.Pet.Add(potion.Uid, -1, amount), Is.EqualTo(StringCode.s_item_err_invalid_count));
            Assert.That(session.Item.Inventory.Get(potion.Uid)?.Amount, Is.EqualTo(3));
            Assert.That(bag.Count, Is.Zero);
        });
    }

    // The field's one-second check: a worn item running out changes the count the totals were built with, so the
    // tick rebuilds them once; the next tick finds the rebuilt count and does nothing.
    [Test]
    public void TheTick_RebuildsOnceWhenAWornItemRunsOut() {
        var test = new TestGameSession();
        Item gear = test.Store(TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        gear.ExpiryTime = now + 1;
        int built = StatsManager.ExpiredWorn([gear], now);

        Assert.Multiple(() => {
            Assert.That(StatsManager.ExpiryChanged(built, StatsManager.ExpiredWorn([gear], now)), Is.False);
            Assert.That(StatsManager.ExpiryChanged(built, StatsManager.ExpiredWorn([gear], now + 1)), Is.True);
            built = StatsManager.ExpiredWorn([gear], now + 1);
            Assert.That(StatsManager.ExpiryChanged(built, StatsManager.ExpiredWorn([gear], now + 2)), Is.False);
        });
    }

    // A worn item that runs out stays worn, but its effects (and its gems') leave with its stats, as an unequip
    // takes them: the client rebuilds the abilities from the items it can use only (equipment.cpp:2950, :3021).
    // An effect another counted item gives too stays, and an effect not held is not removed.
    [Test]
    public void AWornItemRunningOut_LosesItsEffects() {
        int[] expiredEarring = [90050816, 90050820];
        int[] wornRing = [90050820];
        var held = new System.Collections.Generic.HashSet<int> { 90050816, 90050820 };

        Assert.Multiple(() => {
            Assert.That(BuffManager.ItemEffectsToTake(expiredEarring, wornRing, held.Contains), Is.EqualTo(new[] { 90050816 }));
            Assert.That(BuffManager.ItemEffectsToTake(expiredEarring, [], held.Contains), Is.EqualTo(new[] { 90050816, 90050820 }));
            held.Remove(90050816);
            Assert.That(BuffManager.ItemEffectsToTake(expiredEarring, wornRing, held.Contains), Is.Empty);
            Assert.That(BuffManager.ItemEffectsToTake([], wornRing, held.Contains), Is.Empty);
        });
    }

    // An item in the bag past its time stays there with no notice; it cannot be used (pets and maid furnishings
    // excepted, CItem 0x140528980) nor worn (s_item_err_puton_expired).
    [Test]
    public void ABagItemRunningOut_StaysButCannotBeUsedOrWorn() {
        var test = new TestGameSession();
        Item earring = test.Store(TestGameSession.Metadata(11260101, EquipSlot.EA, type: 1));
        Item potion = test.Store(TestGameSession.Metadata(20000001, type: 2), amount: 3);
        GameSession session = test.Start();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.Item.Inventory.Get(earring.Uid)!.ExpiryTime = now;
        session.Item.Inventory.Get(potion.Uid)!.ExpiryTime = now;

        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Get(earring.Uid), Is.Not.Null);
            Assert.That(EquipManager.ValidateEquipItem(session, session.Item.Inventory.Get(earring.Uid)!), Is.EqualTo(StringCode.s_item_err_puton_expired));
            Assert.That(ItemLockRules.ExpiryAllowsUse(session.Item.Inventory.Get(potion.Uid)!.IsExpired(), 2, 0), Is.False);
            Assert.That(session.Item.Inventory.Get(potion.Uid)?.Amount, Is.EqualTo(3));
        });
    }
}

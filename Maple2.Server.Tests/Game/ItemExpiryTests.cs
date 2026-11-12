using System;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
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
}

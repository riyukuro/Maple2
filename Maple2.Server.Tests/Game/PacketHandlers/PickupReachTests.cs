using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestItemPickup 0x1C and RequestMesoPickup 0x1D: the client asks only for a drop within its picker's range
// (CDropItem::FUN_141190120 / FUN_1411903c0), so a drop across the map is refused and stays on the ground.
public class PickupReachTests {
    private const int Stackable = 20000002;
    private const int Meso = 90000001;
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Stackable), TestGameSession.Metadata(Meso));
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private static FieldItem Drop(GameSession session, int itemId, float away) {
        Item item = session.Field!.ItemDrop.CreateItem(itemId)!;
        Vector3 at = session.Player.Position + new Vector3(away, 0, 0);
        return session.Field.SpawnItem(at, session.Player.Rotation, item, null);
    }

    private string[] Refusals(int objectId) {
        string refuse = Convert.ToHexString(ItemPickupPacket.Refuse(objectId).ToArray());
        return test.TakeSent().Select(Convert.ToHexString).Where(packet => packet == refuse).ToArray();
    }

    private static void PickItem(GameSession session, FieldItem drop, int pickerId = 0) {
        new ItemPickupHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(drop.ObjectId);
            writer.WriteBool(pickerId != 0);
            if (pickerId != 0) {
                writer.WriteInt(pickerId);
            }
        }));
    }

    private static void PickMeso(GameSession session, FieldItem drop) {
        new MesoPickupHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(drop.ObjectId);
        }));
    }

    // KR DropItemMaxLength 300: the reach is the larger of it and the throw, plus the throw and the lag (1350).
    [TestCase(0f, true)]
    [TestCase(1300f, true)]
    [TestCase(1400f, false)]
    [TestCase(5000f, false)]
    public void ItemPickup_TakesOnlyADropInReach(float away, bool taken) {
        GameSession session = Start();
        FieldItem drop = Drop(session, Stackable, away);

        PickItem(session, drop);

        Assert.Multiple(() => {
            Assert.That(session.Field!.TryGetItem(drop.ObjectId, out _), Is.EqualTo(!taken));
            Assert.That(session.Item.Inventory.Find(Stackable).Sum(item => item.Amount), Is.EqualTo(taken ? 1 : 0));
            Assert.That(Refusals(drop.ObjectId), Has.Length.EqualTo(taken ? 0 : 1));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A picker id that is not the summoned pet (none is summoned here) gives no reach of its own.
    [Test]
    public void ItemPickup_APickerThatIsNoSummonedPet_IsRefused() {
        GameSession session = Start();
        FieldItem drop = Drop(session, Stackable, 5000f);

        PickItem(session, drop, pickerId: 12345);

        Assert.Multiple(() => {
            Assert.That(session.Field!.TryGetItem(drop.ObjectId, out _), Is.True);
            Assert.That(Refusals(drop.ObjectId), Has.Length.EqualTo(1));
        });
    }

    // A summoned pet with the default follow distance (300), the player standing far away.
    private static int Summon(GameSession session, Vector3 at) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var fieldPet = (FieldPet) RuntimeHelpers.GetUninitializedObject(typeof(FieldPet));
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", any)!.SetValue(fieldPet, 777);
        typeof(Actor<Npc>).GetField("<Transform>k__BackingField", any)!.SetValue(fieldPet, new Transform());
        typeof(FieldPet).GetField(nameof(FieldPet.Metadata), any)!.SetValue(fieldPet, RuntimeHelpers.GetUninitializedObject(typeof(PetMetadata)));
        fieldPet.Position = at;
        var manager = (PetManager) RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
        typeof(PetManager).GetField("session", any)!.SetValue(manager, session);
        typeof(PetManager).GetField("pet", any)!.SetValue(manager, fieldPet);
        session.Pet = manager;
        return fieldPet.ObjectId;
    }

    // StateSummonItemPick: the pet loots a drop next to itself, out of the player's reach. The reach is the pet's
    // server position plus its follow distance; the player's own request without the pet id is refused.
    [TestCase(4900f, true, true)]
    [TestCase(4900f, false, false)]
    [TestCase(0f, true, false)]
    public void ItemPickup_ASummonedPetInReach_TakesTheDrop(float petAway, bool petPicks, bool taken) {
        GameSession session = Start();
        FieldItem drop = Drop(session, Stackable, 5000f);
        int petId = Summon(session, session.Player.Position + new Vector3(petAway, 0, 0));

        PickItem(session, drop, pickerId: petPicks ? petId : 0);

        Assert.Multiple(() => {
            Assert.That(session.Field!.TryGetItem(drop.ObjectId, out _), Is.EqualTo(!taken));
            Assert.That(Refusals(drop.ObjectId), Has.Length.EqualTo(taken ? 0 : 1));
        });
    }

    [TestCase(0f, true)]
    [TestCase(5000f, false)]
    public void MesoPickup_TakesOnlyADropInReach(float away, bool taken) {
        GameSession session = Start();
        FieldItem drop = Drop(session, Meso, away);
        long before = session.Currency.Meso;

        PickMeso(session, drop);

        Assert.Multiple(() => {
            Assert.That(session.Field!.TryGetItem(drop.ObjectId, out _), Is.EqualTo(!taken));
            Assert.That(session.Currency.Meso > before, Is.EqualTo(taken));
            Assert.That(Refusals(drop.ObjectId), Has.Length.EqualTo(taken ? 0 : 1));
        });
    }

    [Test]
    public void Reach_IsTheClientsRangePlusTheThrow() {
        Assert.Multiple(() => {
            Assert.That(PickupRules.Reach(300), Is.EqualTo(1350f));
            Assert.That(PickupRules.Reach(0), Is.EqualTo(1350f));
            Assert.That(PickupRules.Reach(1000), Is.EqualTo(1750f));
            // The height is the client's: a drop below the picker on the ground plane is in reach.
            Assert.That(PickupRules.InReach(Vector3.Zero, new Vector3(100, 0, -3000), 1350f), Is.True);
            Assert.That(PickupRules.InReach(Vector3.Zero, new Vector3(float.NaN, 0, 0), 1350f), Is.False);
        });
    }
    // Dead for deadMs: the client's loot tick sends no pickup of the player's own while it is dead
    // (MyPC::FUN_141530db0, mypc.cpp:4713), but one it sent before it heard of the death may still come.
    private static void Dead(GameSession session, long deadMs) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        typeof(FieldPlayer).GetField("isDead", any)!.SetValue(session.Player, true);
        typeof(FieldPlayer).GetField("deadSinceTick", any)!.SetValue(session.Player, Environment.TickCount64 - deadMs);
    }

    // Dead past the race grace: the player's item and meso pickups are refused (answered, so the client asks again
    // after the revival) and the drops stay. Before, both were taken. Just dead, the client may not know yet.
    [TestCase(PickupRules.DeadRaceGraceMs + 1000, false)]
    [TestCase(0, true)]
    public void Pickups_OfADeadPlayer(long deadMs, bool taken) {
        GameSession session = Start();
        FieldItem item = Drop(session, Stackable, 0f);
        FieldItem meso = Drop(session, Meso, 0f);
        Dead(session, deadMs);

        PickItem(session, item);
        PickMeso(session, meso);

        Assert.Multiple(() => {
            Assert.That(session.Field!.TryGetItem(item.ObjectId, out _), Is.EqualTo(!taken));
            Assert.That(session.Field!.TryGetItem(meso.ObjectId, out _), Is.EqualTo(!taken));
            Assert.That(session.Item.Inventory.Find(Stackable).Sum(entry => entry.Amount), Is.EqualTo(taken ? 1 : 0));
        });
    }

    // A pet's pickup has no test of its owner's death in the client (statesummon.cpp:909): it is served.
    [Test]
    public void ItemPickup_ByThePetOfADeadPlayer_IsTaken() {
        GameSession session = Start();
        FieldItem drop = Drop(session, Stackable, 0f);
        int petId = Summon(session, session.Player.Position);
        Dead(session, PickupRules.DeadRaceGraceMs + 1000);

        PickItem(session, drop, pickerId: petId);

        Assert.That(session.Field!.TryGetItem(drop.ObjectId, out _), Is.False);
    }

    // A battle mount (KR 99910340) on for mountedMs: owned by the player or by another (the player a passenger).
    private static void Mount(GameSession session, bool disablePickUp, long mountedMs, bool owner = true) {
        var metadata = new RideMetadata(99910340, "", new RideMetadataBasic(RideOnType.Battle, 0, 0, 0, false, 0, 1, disablePickUp),
            new RideMetadataSpeed(0, 0, 0, 0), new Dictionary<BasicAttribute, long>());
        session.Ride = new RideManager(session);
        int ownerId = owner ? session.Player.ObjectId : session.Player.ObjectId + 1;
        session.Ride.Ride = new Ride(ownerId, metadata, new RideOnActionBattle(metadata.Id, 4242, 0, 1)) {
            StartTick = Environment.TickCount64 - mountedMs,
        };
    }

    // The client's loot tick sends nothing on a disablePickUp ride (mypc.cpp:4716-4719): the owner's item and meso
    // pickups are refused once the ride has had time to reach the client. Before, both were taken. A ride without the
    // flag, a ride just started, and a passenger (not proven in the client) are served.
    [TestCase(true, PickupRules.RideRaceGraceMs + 1000, true, false)]
    [TestCase(true, 0, true, true)]
    [TestCase(false, PickupRules.RideRaceGraceMs + 1000, true, true)]
    [TestCase(true, PickupRules.RideRaceGraceMs + 1000, false, true)]
    public void Pickups_OnADisablePickUpRide(bool disablePickUp, long mountedMs, bool owner, bool taken) {
        GameSession session = Start();
        FieldItem item = Drop(session, Stackable, 0f);
        FieldItem meso = Drop(session, Meso, 0f);
        Mount(session, disablePickUp, mountedMs, owner);

        PickItem(session, item);
        PickMeso(session, meso);

        Assert.Multiple(() => {
            Assert.That(session.Field!.TryGetItem(item.ObjectId, out _), Is.EqualTo(!taken));
            Assert.That(session.Field!.TryGetItem(meso.ObjectId, out _), Is.EqualTo(!taken));
            Assert.That(Refusals(item.ObjectId), Has.Length.EqualTo(taken ? 0 : 1));
        });
    }

    // The pet's pickup is the pet's (statesummon.cpp:909), not the loot tick's: it is served on such a ride.
    [Test]
    public void ItemPickup_ByThePetOnADisablePickUpRide_IsTaken() {
        GameSession session = Start();
        FieldItem drop = Drop(session, Stackable, 0f);
        int petId = Summon(session, session.Player.Position);
        Mount(session, true, PickupRules.RideRaceGraceMs + 1000);

        PickItem(session, drop, pickerId: petId);

        Assert.That(session.Field!.TryGetItem(drop.ObjectId, out _), Is.False);
    }
}

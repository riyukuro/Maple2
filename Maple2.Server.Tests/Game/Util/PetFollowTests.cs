using System.Numerics;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// A summoned pet follows its owner on the server (the client sends no pet movement).
public class PetFollowTests {
    private static readonly Vector3 Owner = new(1000, 2000, 300);

    [Test]
    public void CloseByStays() {
        Vector3 pet = Owner + new Vector3(PetFollow.FollowDistance - 1, 0, 0);
        Assert.Multiple(() => {
            Assert.That(PetFollow.Decide(pet, Owner, out Vector3 target), Is.EqualTo(PetFollow.Action.Stay));
            Assert.That(target, Is.EqualTo(pet));
        });
    }

    [Test]
    public void FartherWalksToTheStopPointOnItsSide() {
        Vector3 pet = Owner + new Vector3(0, -800, 20);
        Assert.Multiple(() => {
            Assert.That(PetFollow.Decide(pet, Owner, out Vector3 target), Is.EqualTo(PetFollow.Action.Walk));
            Assert.That(target.X, Is.EqualTo(Owner.X).Within(0.01f));
            Assert.That(target.Y, Is.EqualTo(Owner.Y - PetFollow.StopDistance).Within(0.01f));
            Assert.That(target.Z, Is.EqualTo(Owner.Z));
        });
    }

    // A GM coordinate move puts the owner far away: the pet is put next to the owner.
    [Test]
    public void TeleportedOwnerWarpsThePet() {
        Vector3 pet = Owner + new Vector3(-20000, 15000, 0);
        Assert.Multiple(() => {
            Assert.That(PetFollow.Decide(pet, Owner, out Vector3 target), Is.EqualTo(PetFollow.Action.Warp));
            Assert.That(target, Is.EqualTo(Owner));
        });
    }

    [Test]
    public void AnotherFloorWarpsThePet() {
        Vector3 pet = Owner + new Vector3(100, 0, -(PetFollow.WarpHeight + 1));
        Assert.That(PetFollow.Decide(pet, Owner, out _), Is.EqualTo(PetFollow.Action.Warp));
    }

    // The broken goals in the logs had Y = -21474788: a target that far is never walked to.
    [Test]
    public void NoWalkTargetFarFromTheOwner() {
        Vector3 pet = new(-1200, -21474788, 300);
        Assert.Multiple(() => {
            Assert.That(PetFollow.Decide(pet, Owner, out Vector3 target), Is.EqualTo(PetFollow.Action.Warp));
            Assert.That(target, Is.EqualTo(Owner));
            Assert.That(PetFollow.Decide(new Vector3(float.NaN, 0, 0), Owner, out target), Is.EqualTo(PetFollow.Action.Warp));
            Assert.That(target, Is.EqualTo(Owner));
            Assert.That(PetFollow.Decide(Owner, new Vector3(float.PositiveInfinity, 0, 0), out _), Is.EqualTo(PetFollow.Action.Stay));
        });
    }

    // Runaway nodes carry till="1200"; the preset's distance was float.MaxValue, and (int)float.MaxValue is
    // int.MinValue, which aimed the path about 2^31 units away.
    [TestCase(1200, null, 1200f)]
    [TestCase(0, 800f, 800f)]
    [TestCase(0, null, AiRunaway.DefaultRunawayDistance)]
    [TestCase(0, float.MaxValue, AiRunaway.MaxRunawayDistance)]
    [TestCase(100000, null, AiRunaway.MaxRunawayDistance)]
    [TestCase(-5, 0f, AiRunaway.DefaultRunawayDistance)]
    public void RunawayDistance(int till, float? skillDistance, float expected) {
        float distance = AiRunaway.Distance(till, skillDistance);
        Assert.Multiple(() => {
            Assert.That(distance, Is.EqualTo(expected));
            Assert.That((int) distance, Is.Positive);
        });
    }

    // CPetManager::OnPetUnsummoned prints "You dismissed your pet." for the local player's pet.
    [Test]
    public void FieldChangeUnSummonSkipsTheOwner() {
        Assert.Multiple(() => {
            Assert.That(PetManager.UnSummonToOwner(fieldChange: true), Is.False);
            Assert.That(PetManager.UnSummonToOwner(fieldChange: false), Is.True);
        });
    }

    // The client reads an int owner object id and a long pet uid after the mode.
    [Test]
    public void UnSummonPacket() {
        ByteWriter writer = PetPacket.UnSummon(0x01020304, 0x1122334455667788L);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponsePet));
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(0x01020304));
            Assert.That(reader.ReadLong(), Is.EqualTo(0x1122334455667788L));
            Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 8));
        });
    }
}

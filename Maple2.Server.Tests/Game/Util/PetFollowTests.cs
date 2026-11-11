using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Metadata;
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

    // petproperty rows give warpDistance/traceDistance 3000/300, 1200/200 or 1200/300; a row without them takes
    // the fallback constants, and a warp under the trace distance is raised to it.
    [TestCase(3000f, 300f, 300f, 3000f)]
    [TestCase(1200f, 200f, 200f, 1200f)]
    [TestCase(1200f, 300f, 300f, 1200f)]
    [TestCase(0f, 0f, PetFollow.FollowDistance, PetFollow.WarpDistance)]
    [TestCase(-1f, float.NaN, PetFollow.FollowDistance, PetFollow.WarpDistance)]
    [TestCase(100f, 300f, 300f, 300f)]
    public void RowDistances(float warp, float trace, float follow, float expectedWarp) {
        (float Follow, float Warp) distances = PetFollow.Distances(new PetMetadataDistance(warp, trace, 0));
        Assert.Multiple(() => {
            Assert.That(distances.Follow, Is.EqualTo(follow));
            Assert.That(distances.Warp, Is.EqualTo(expectedWarp));
        });
    }

    [Test]
    public void NoRowTakesTheFallback() {
        Assert.That(PetFollow.Distances(null), Is.EqualTo((PetFollow.FollowDistance, PetFollow.WarpDistance)));
    }

    // A trace distance of 200: 250 away walks (the fallback 300 would stay), and the walk ends inside 200.
    [Test]
    public void RowTraceDistanceStartsTheWalk() {
        var row = new PetMetadataDistance(1200, 200, 0);
        Vector3 pet = Owner + new Vector3(250, 0, 0);
        Assert.Multiple(() => {
            Assert.That(PetFollow.Decide(pet, Owner, out _), Is.EqualTo(PetFollow.Action.Stay));
            Assert.That(PetFollow.Decide(pet, Owner, row, out Vector3 target), Is.EqualTo(PetFollow.Action.Walk));
            Assert.That(Vector3.Distance(target, Owner), Is.LessThan(200f));
            Assert.That(PetFollow.Decide(target, Owner, row, out _), Is.EqualTo(PetFollow.Action.Stay));
        });
    }

    // A warp distance of 1200 warps a pet 1300 away; one of 3000 walks it from 2000 (the fallback 1500 warps).
    [Test]
    public void RowWarpDistance() {
        Assert.Multiple(() => {
            Assert.That(PetFollow.Decide(Owner + new Vector3(0, 1300, 0), Owner, new PetMetadataDistance(1200, 300, 0), out _),
                Is.EqualTo(PetFollow.Action.Warp));
            Assert.That(PetFollow.Decide(Owner + new Vector3(0, 2000, 0), Owner, new PetMetadataDistance(3000, 300, 5000), out _),
                Is.EqualTo(PetFollow.Action.Walk));
            Assert.That(PetFollow.Decide(Owner + new Vector3(0, 2000, 0), Owner, out _), Is.EqualTo(PetFollow.Action.Warp));
        });
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

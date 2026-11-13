using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class TombstoneRulesTests {
    // Tombstone 0x002A: i32 object id, i32 hits. The hitter here has object id 7.
    [TestCase(9, 1, true)]
    [TestCase(7, 1, false)] // the own tombstone
    [TestCase(9, 0, false)]
    [TestCase(9, 2, false)]
    [TestCase(9, -1, false)]
    [TestCase(9, 255, false)]
    [TestCase(9, 257, false)] // 1 after a cut to a byte
    public void HitPacket(int objectId, int hits, bool expected) {
        var writer = new ByteWriter();
        writer.WriteInt(objectId);
        writer.WriteInt(hits);
        var packet = new ByteReader(writer.ToArray(), 0);
        int owner = packet.ReadInt();
        Assert.That(TombstoneRules.IsHit(packet.ReadInt(), owner, 7), Is.EqualTo(expected));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [TestCase((byte) 3, true)]
    [TestCase((byte) 1, true)]
    [TestCase((byte) 0, false)] // already broken: no hit, no hit_tombstone count
    public void BrokenTombstoneTakesNoHit(byte remaining, bool expected) {
        Assert.That(TombstoneRules.CanHit(remaining), Is.EqualTo(expected));
    }

    [TestCase((byte) 3, (byte) 2)]
    [TestCase((byte) 1, (byte) 0)]
    [TestCase((byte) 0, (byte) 0)]
    public void OneHitLess(byte remaining, byte expected) {
        Assert.That(TombstoneRules.AfterHit(remaining), Is.EqualTo(expected));
    }

    // A death within timeResetDead (180 s) of the one before adds to the row; the time runs from death to death.
    [TestCase(0, 0L, 1000L, 1)]            // the first death
    [TestCase(1, 1000L, 15000L, 2)]        // 14 s later
    [TestCase(2, 1000L, 176000L, 3)]       // 175 s later
    [TestCase(5, 1000L, 100000L, 6)]       // the row goes on past the cap; the hit total holds it
    [TestCase(3, 1000L, 181000L, 1)]       // 180 s: a new row
    [TestCase(3, 1000L, 546000L, 1)]
    [TestCase(2, 5000L, 1000L, 1)]         // a clock that went back starts a new row
    public void DeathsInARow(int previous, long previousMs, long nowMs, int expected) {
        Assert.That(TombstoneRules.DeathStreak(previous, previousMs, nowMs, 180), Is.EqualTo(expected));
    }

    // hitPerDeadCount 5, maxDeadCount 3: 5, 10, 15 and no more.
    [TestCase(1, (byte) 5)]
    [TestCase(2, (byte) 10)]
    [TestCase(3, (byte) 15)]
    [TestCase(4, (byte) 15)]
    [TestCase(40, (byte) 15)]
    [TestCase(0, (byte) 5)]
    public void HitsOfATombstone(int streak, byte expected) {
        Assert.That(TombstoneRules.TotalHits(streak, 5, 3), Is.EqualTo(expected));
    }

    [Test]
    public void HitsOfATombstone_StayInAByte() {
        Assert.That(TombstoneRules.TotalHits(100, 200, 100), Is.EqualTo((byte) 255));
        Assert.That(TombstoneRules.TotalHits(2, 5, 0), Is.EqualTo((byte) 5));
    }

    // StateSummonRevive sends Revival 3 when the owner is dead and PC::IsRevivalBlocked is false: no death count,
    // no dark tombstone (both DeathState.Metal), no disableRevivalHere buff.
    [TestCase(true, true, DeathState.FirstDeath, false, true)]
    [TestCase(true, true, DeathState.Metal, false, false)]
    [TestCase(true, true, DeathState.FirstDeath, true, false)]
    [TestCase(true, false, DeathState.FirstDeath, false, false)] // no tombstone
    [TestCase(false, true, DeathState.Alive, false, false)]
    public void PetHelpsTheOwnerUp(bool dead, bool tombstone, DeathState state, bool buff, bool expected) {
        Assert.That(TombstoneRules.PetHelps(dead, tombstone, state, buff), Is.EqualTo(expected));
    }

    [Test]
    public void PetHoldsItsStateWhileTheOwnerLiesDead() {
        Assert.That(TombstoneRules.PetHolds(true, true), Is.True); // also on a metal tombstone: the pet plays Tired_A
        Assert.That(TombstoneRules.PetHolds(true, false), Is.False);
        Assert.That(TombstoneRules.PetHolds(false, false), Is.False);
    }

    [Test]
    public void PetHitsComeOncePerState() {
        Assert.That(TombstoneRules.PetHitTooSoon(13_000, 1_000, 10_000), Is.False);
        Assert.That(TombstoneRules.PetHitTooSoon(12_500, 1_000, 10_000), Is.False);
        Assert.That(TombstoneRules.PetHitTooSoon(12_499, 1_000, 10_000), Is.True);
        Assert.That(TombstoneRules.PetHitTooSoon(10_001, 1_000, 10_000), Is.True);
    }

    [Test]
    public void ThePetsFirstHitWaitsForItsRevivePass() {
        // No hit yet (lastTick 0): the floor runs from the pet's entry into its revive state, not from nothing.
        Assert.That(TombstoneRules.PetHitTooSoon(10_000, 10_000, 0), Is.True);
        Assert.That(TombstoneRules.PetHitTooSoon(12_499, 10_000, 0), Is.True);
        Assert.That(TombstoneRules.PetHitTooSoon(12_500, 10_000, 0), Is.False);
        Assert.That(TombstoneRules.PetHitTooSoon(13_000, 10_000, 0), Is.False);
        // A later death: the new entry, not the last death's hit, starts the floor.
        Assert.That(TombstoneRules.PetHitTooSoon(21_000, 20_000, 10_000), Is.True);
        Assert.That(TombstoneRules.PetHitTooSoon(22_500, 20_000, 10_000), Is.False);
    }
}

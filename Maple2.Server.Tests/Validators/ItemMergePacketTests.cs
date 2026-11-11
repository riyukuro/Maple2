using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class ItemMergePacketTests {
    // ItemMerge 0x003F mode 0 has no payload; the answer the client reads is u8 mode 0, u8 0.
    [Test]
    public void InfoAnswerIsModeZeroAndNothingPending() {
        byte[] bytes = ItemMergePacket.NoPendingChoice().ToArray();
        Assert.That(bytes.Length, Is.EqualTo(4));
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    // Mode 0 with a waiting result: u8 1, int item id, long item uid, then the three ints of mode 4.
    [Test]
    public void InfoAnswerWithPendingChoice() {
        byte[] bytes = ItemMergePacket.PendingChoice(11200001, 77L, new MergeRoll(BasicAttribute.Dexterity, SpecialAttribute.None, 12)).ToArray();
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.ReadByte(), Is.EqualTo(1));
        Assert.That(packet.ReadInt(), Is.EqualTo(11200001));
        Assert.That(packet.ReadLong(), Is.EqualTo(77L));
        Assert.That(packet.ReadInt(), Is.EqualTo((int) BasicAttribute.Dexterity));
        Assert.That(packet.ReadInt(), Is.EqualTo(0));
        Assert.That(packet.ReadInt(), Is.EqualTo(12));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    // Mode 0, u8 2: only the item id follows.
    [Test]
    public void InfoAnswerWithMissingItem() {
        byte[] bytes = ItemMergePacket.PendingItemMissing(11200001).ToArray();
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.ReadByte(), Is.EqualTo(2));
        Assert.That(packet.ReadInt(), Is.EqualTo(11200001));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    // Mode 4: basic attribute or -1, special attribute, value.
    [Test]
    public void ChoiceOfSpecialOptionHasNoBasicAttribute() {
        byte[] bytes = ItemMergePacket.Choice(new MergeRoll(null, SpecialAttribute.Experience, 35)).ToArray();
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(4));
        Assert.That(packet.ReadInt(), Is.EqualTo(-1));
        Assert.That(packet.ReadInt(), Is.EqualTo((int) SpecialAttribute.Experience));
        Assert.That(packet.ReadInt(), Is.EqualTo(35));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    private static ItemMergeTable.Option RateOption() {
        // A rate attribute: the steps are in Rates, Values stay 0.
        return new ItemMergeTable.Option(
            Values: [new ItemMergeTable.Range<int>(0, 0), new ItemMergeTable.Range<int>(0, 0)],
            Rates: [new ItemMergeTable.Range<int>(11, 20), new ItemMergeTable.Range<int>(21, 30)],
            Weights: [1, 1]);
    }

    [Test]
    public void BoundsCoverTheStepsOfARateOption() {
        Assert.That(RateOption().Bounds(), Is.EqualTo(new ItemMergeTable.Range<int>(11, 30)));
        Assert.That(new ItemMergeTable.Option([], [], []).Bounds(), Is.EqualTo(new ItemMergeTable.Range<int>(0, 0)));
    }

    // The preview (mode 2) carries the range the client shows: not 0 to 0 for a rate option.
    [Test]
    public void SelectWritesTheRangeOfARateOption() {
        var entry = new ItemMergeTable.Entry(1, 100, [],
            new Dictionary<BasicAttribute, ItemMergeTable.Option> { [BasicAttribute.Piercing] = RateOption() },
            new Dictionary<SpecialAttribute, ItemMergeTable.Option>());
        byte[] bytes = ItemMergePacket.Select(entry, 2).ToArray();
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(2));
        Assert.That(packet.ReadLong(), Is.EqualTo(200));
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.ReadInt(), Is.EqualTo(0));
        Assert.That(packet.ReadInt(), Is.EqualTo(1));
        Assert.That(packet.ReadShort(), Is.EqualTo((short) BasicAttribute.Piercing));
        Assert.That(packet.ReadInt(), Is.EqualTo(11));
        Assert.That(packet.ReadInt(), Is.EqualTo(30));
        Assert.That(packet.ReadInt(), Is.EqualTo(0));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [Test]
    public void RollStaysInsideTheStepsAndReachesBothEnds() {
        var entry = new ItemMergeTable.Entry(1, 100, [],
            new Dictionary<BasicAttribute, ItemMergeTable.Option> { [BasicAttribute.Piercing] = RateOption() },
            new Dictionary<SpecialAttribute, ItemMergeTable.Option>());
        var random = new Random(7);
        var seen = new HashSet<int>();
        for (int i = 0; i < 2000; i++) {
            MergeRoll roll = ItemMergeManager.Roll(entry, random);
            Assert.That(roll.Basic, Is.EqualTo(BasicAttribute.Piercing));
            Assert.That(roll.Value, Is.InRange(11, 30));
            seen.Add(roll.Value);
        }
        Assert.That(seen, Does.Contain(11));
        Assert.That(seen, Does.Contain(30));
    }

    // The merge option is the only one of its group and the crystal's id is the int after the groups.
    [Test]
    public void MergeOptionReplacesTheOldOneAndTravelsWithTheItemStats() {
        var stats = new ItemStats();
        Assert.That(stats.HasMergeOption, Is.False);
        stats.SetMergeOption(new MergeRoll(BasicAttribute.Dexterity, SpecialAttribute.None, 12), 37000001);
        stats.SetMergeOption(new MergeRoll(null, SpecialAttribute.Experience, 35), 37000002);
        Assert.That(stats.HasMergeOption, Is.True);
        Assert.That(stats[ItemStats.Type.Empowerment].Basic, Is.Empty);
        Assert.That(stats[ItemStats.Type.Empowerment].Special[SpecialAttribute.Experience].Value, Is.EqualTo(35));
        Assert.That(stats.Clone().MergeId, Is.EqualTo(37000002));

        var writer = new ByteWriter();
        stats.WriteTo(writer);
        byte[] bytes = writer.ToArray();
        Assert.That(BitConverter.ToInt32(bytes, bytes.Length - 4), Is.EqualTo(37000002));
        var read = new ItemStats();
        read.ReadFrom(new ByteReader(bytes));
        Assert.That(read.MergeId, Is.EqualTo(37000002));
        Assert.That(read[ItemStats.Type.Empowerment].Special[SpecialAttribute.Experience].Value, Is.EqualTo(35));
    }
}

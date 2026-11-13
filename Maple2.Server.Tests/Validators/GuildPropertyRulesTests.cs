using System.Collections.Generic;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class GuildPropertyRulesTests {
    private static GuildTable.Property Row(short level, int experience, byte capacity) {
        return new GuildTable.Property(level, experience, capacity, 0, 0, level * 10, 0, 0, 0, 0, 0, 0, 0, 0f, 0f, 0, 0, 0, 0);
    }

    // The guild.xml levels 1 to 6 and the last two: a row's experience is what the next level needs.
    private static readonly IReadOnlyDictionary<short, GuildTable.Property> Properties = new Dictionary<short, GuildTable.Property> {
        [4] = Row(4, 140000, 70),
        [1] = Row(1, 10000, 60),
        [2] = Row(2, 30000, 60),
        [3] = Row(3, 70000, 60),
        [5] = Row(5, 250000, 70),
        [6] = Row(6, 410000, 70),
        [14] = Row(14, 3740000, 100),
        [15] = Row(15, 4350000, 100),
    };

    [TestCase(0, 1)]
    [TestCase(9999, 1)]
    [TestCase(10000, 2)]
    [TestCase(69999, 3)]
    [TestCase(70000, 4)]
    [TestCase(139999, 4)]
    [TestCase(140000, 5)]
    [TestCase(4349999, 15)]
    [TestCase(4350000, 15)]
    [TestCase(int.MaxValue, 15)]
    public void PropertyAtTakesTheClientLevel(int experience, int level) {
        Assert.That(GuildRules.PropertyAt(Properties, experience)!.Level, Is.EqualTo(level));
    }

    [Test]
    public void CapacityRisesWithTheLevel() {
        Assert.Multiple(() => {
            Assert.That(GuildRules.PropertyAt(Properties, 69999)!.Capacity, Is.EqualTo(60));
            Assert.That(GuildRules.PropertyAt(Properties, 70000)!.Capacity, Is.EqualTo(70));
            Assert.That(GuildRules.PropertyAt(Properties, 3740000)!.Capacity, Is.EqualTo(100));
        });
    }

    [Test]
    public void CheckInExpFollowsTheLevel() {
        Assert.That(GuildRules.PropertyAt(Properties, 250000)!.CheckInExp, Is.EqualTo(60));
    }

    [Test]
    public void EmptyTableHasNoRow() {
        Assert.That(GuildRules.PropertyAt(new Dictionary<short, GuildTable.Property>(), 0), Is.Null);
    }

    [Test]
    public void NewGuildHasTheLevelOneCap() {
        Assert.That(new Guild(1, "g", 2, 3, "m").Capacity, Is.EqualTo(GuildRules.DefaultCapacity));
    }

    // Guild 28 after the mode byte: a name the client skips, then the cap as an int.
    [Test]
    public void NotifyUpdateCapacityLayout() {
        byte[] bytes = GuildPacket.NotifyUpdateCapacity(string.Empty, 70).ToArray();
        var packet = new ByteReader(bytes, 0);
        packet.ReadShort();
        Assert.Multiple(() => {
            Assert.That(packet.ReadByte(), Is.EqualTo(28));
            Assert.That(packet.ReadUnicodeString(), Is.EqualTo(string.Empty));
            Assert.That(packet.ReadInt(), Is.EqualTo(70));
            Assert.That(packet.Available, Is.EqualTo(0));
        });
    }
}

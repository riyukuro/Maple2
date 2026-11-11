using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Client: both stat readers take count 0x23. The owner's reader (MyPC::vfunc_90) reads every stat, three values each,
// stat 4 as long and the rest as int. The reader for other players (PC::vfunc_90) reads five stats, three values each:
// long stat 4, int stats 0xd, 0xe, 0x20, 0x16.
public class StatLayoutTests {
    private static Stats Sample() {
        return new Stats(new Dictionary<BasicAttribute, long> {
            { BasicAttribute.Strength, 11 },
            { BasicAttribute.Health, 5000 },
            { BasicAttribute.AttackSpeed, 120 },
            { BasicAttribute.MovementSpeed, 130 },
            { BasicAttribute.JumpHeight, 140 },
        }, JobCode.Newbie);
    }

    [Test]
    public void OwnerFormIsEveryStat() {
        var writer = new ByteWriter();
        writer.WriteAllStats(Sample());
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(0x23));
        for (int type = 0; type < 0x23; type++) {
            for (int i = 0; i < 3; i++) {
                long value = type == 4 ? reader.ReadLong() : reader.ReadInt();
                if (i == 0 && type == 0) {
                    Assert.That(value, Is.EqualTo(11));
                }
                if (i == 0 && type == 4) {
                    Assert.That(value, Is.EqualTo(5000));
                }
            }
        }
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void OtherPlayerFormIsFiveStats() {
        var writer = new ByteWriter();
        writer.WritePlayerStats(Sample());
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(0x23));
        for (int i = 0; i < 3; i++) {
            long health = reader.ReadLong();
            int attackSpeed = reader.ReadInt();
            int moveSpeed = reader.ReadInt();
            int mountSpeed = reader.ReadInt();
            int jumpHeight = reader.ReadInt();
            if (i == 0) {
                Assert.That(health, Is.EqualTo(5000));
                Assert.That(attackSpeed, Is.EqualTo(120));
                Assert.That(moveSpeed, Is.EqualTo(130));
                Assert.That(mountSpeed, Is.EqualTo(100));
                Assert.That(jumpHeight, Is.EqualTo(140));
            }
        }
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // The two forms share the count byte and nothing else, so one must never be sent in place of the other.
    [Test]
    public void FormsDifferInLength() {
        var owner = new ByteWriter();
        owner.WriteAllStats(Sample());
        var other = new ByteWriter();
        other.WritePlayerStats(Sample());
        Assert.That(owner.Length, Is.EqualTo(1 + 3 * (8 + 34 * 4)));
        Assert.That(other.Length, Is.EqualTo(1 + 3 * (8 + 4 * 4)));
    }
}

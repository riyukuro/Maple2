using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.Packets;

// The client's basic stat ids (table/statstringtable.xml) have 29 bap and 30 bap_pet where the server's enum has
// BonusAtk 33 and PetBonusAtk 34 (BasicAttributeIds). Every packet field of a basic stat id carries the client's id;
// the models keep the server's.
public class StatIdTests {
    [Test]
    public void RoundTrips() {
        for (int id = 0; id < 0x23; id++) {
            Assert.That(BasicAttributeIds.ServerStat(id).ClientStat(), Is.EqualTo(id));
        }
        Assert.That(BasicAttribute.BonusAtk.ClientStat(), Is.EqualTo(29));
        Assert.That(BasicAttribute.PetBonusAtk.ClientStat(), Is.EqualTo(30));
        Assert.That(BasicAttribute.Piercing.ClientStat(), Is.EqualTo(31));
        Assert.That(BasicAttribute.MountSpeed.ClientStat(), Is.EqualTo(32));
    }

    // CItemOption::Deserialize: per group i16 count, count x (i16 stat id, option), i16 count of specials.
    [Test]
    public void ItemOption_BonusAttackIs29() {
        var basic = new Dictionary<BasicAttribute, BasicOption>[ItemStats.TYPE_COUNT];
        for (int i = 0; i < basic.Length; i++) {
            basic[i] = new Dictionary<BasicAttribute, BasicOption>();
        }
        basic[(int) ItemStats.Type.Static][BasicAttribute.BonusAtk] = new BasicOption(52);
        var stats = new ItemStats(basic, []);
        var writer = new ByteWriter();
        stats.WriteTo(writer);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.ReadShort(), Is.EqualTo(1));
        Assert.That(reader.ReadShort(), Is.EqualTo(29));
        Assert.That(reader.ReadInt(), Is.EqualTo(52));

        var copy = new ItemStats();
        copy.ReadFrom(new ByteReader(writer.ToArray(), 0));
        Assert.That(copy[ItemStats.Type.Static].Basic[BasicAttribute.BonusAtk], Is.EqualTo(new BasicOption(52)));
    }

    [Test]
    public void Enchant_BonusAttackIs29() {
        var enchant = new ItemEnchant(basicOptions: new Dictionary<BasicAttribute, BasicOption> {
            { BasicAttribute.PetBonusAtk, new BasicOption(7) },
        });
        var writer = new ByteWriter();
        enchant.WriteTo(writer);
        byte[] bytes = writer.ToArray();
        var tail = new ByteReader(bytes, bytes.Length - 9);
        Assert.That(tail.ReadByte(), Is.EqualTo(1));
        Assert.That(tail.ReadInt(), Is.EqualTo(30));
        Assert.That(tail.ReadInt(), Is.EqualTo(7));

        var copy = new ItemEnchant();
        copy.ReadFrom(new ByteReader(bytes, 0));
        Assert.That(copy.BasicOptions[BasicAttribute.PetBonusAtk], Is.EqualTo(new BasicOption(7)));
    }

    [Test]
    public void LimitBreak_BonusAttackIs29() {
        var limitBreak = new ItemLimitBreak(3, new Dictionary<BasicAttribute, BasicOption> {
            { BasicAttribute.BonusAtk, new BasicOption(40) },
        }, new Dictionary<SpecialAttribute, SpecialOption>());
        var writer = new ByteWriter();
        limitBreak.WriteTo(writer);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadShort(), Is.EqualTo(29));
        Assert.That(reader.ReadInt(), Is.EqualTo(40));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
    }

    // A kept basic option of 0x0075/0x0076 names the client's id; a special option's id is unchanged.
    [Test]
    public void Lock_ClientIdToServer() {
        var writer = new ByteWriter();
        writer.WriteByte(2);
        writer.WriteByte(0);
        writer.WriteShort(29);
        writer.WriteByte(1);
        writer.WriteShort(29);
        List<ItemRemakeRules.Lock> locks = ItemRemakeRules.ReadLocks(new ByteReader(writer.ToArray(), 0));
        Assert.That(locks[0], Is.EqualTo(new ItemRemakeRules.Lock(false, (short) BasicAttribute.BonusAtk)));
        Assert.That(locks[1], Is.EqualTo(new ItemRemakeRules.Lock(true, 29)));
    }

    // Black market search keys: group, percent, client stat id (FUN_1401bce20).
    [Test]
    public void BlackMarketKey_ClientIdToServer() {
        Assert.That(BlackMarketHandler.ServerStatKey(29), Is.EqualTo((int) BasicAttribute.BonusAtk));
        Assert.That(BlackMarketHandler.ServerStatKey(1030), Is.EqualTo(1000 + (int) BasicAttribute.PetBonusAtk));
        Assert.That(BlackMarketHandler.ServerStatKey(1015), Is.EqualTo(1000 + (int) BasicAttribute.Accuracy));
        Assert.That(BlackMarketHandler.ServerStatKey(11029), Is.EqualTo(11029));
    }
}

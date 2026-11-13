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

    // UnlimitedEnchantInfo::ReadFromStream (unlimitedenchantinfo.cpp:101) reads each special entry as u16 id and a
    // BaseOption, and BaseOption::Read (0x140266d60) is one int (the stream's int read, kept as m_nValue): the
    // special list is short + int per entry, the reader's last field.
    [Test]
    public void LimitBreak_SpecialEntryIsShortAndInt() {
        var limitBreak = new ItemLimitBreak(5, new Dictionary<BasicAttribute, BasicOption>(), new Dictionary<SpecialAttribute, SpecialOption> {
            { SpecialAttribute.Experience, new SpecialOption(30) },
        });
        var writer = new ByteWriter();
        limitBreak.WriteTo(writer);
        byte[] bytes = writer.ToArray();
        var reader = new ByteReader(bytes, 0);
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SpecialAttribute.Experience));
        Assert.That(reader.ReadInt(), Is.EqualTo(30));
        Assert.That(bytes, Has.Length.EqualTo(18));

        var read = new ItemLimitBreak(0, new Dictionary<BasicAttribute, BasicOption>(), new Dictionary<SpecialAttribute, SpecialOption>());
        read.ReadFrom(new ByteReader(bytes, 0));
        Assert.That(read.SpecialOptions[SpecialAttribute.Experience], Is.EqualTo(new SpecialOption(30)));
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
        List<ItemRemakeRules.Lock> locks = ItemRemakeRules.ReadLocks(new ByteReader(writer.ToArray(), 0))!;
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

    // The client's stat table has ids 0..34; an id past it is no stat (it is not cast to the byte enum: 285 is not 29).
    [TestCase(-1)]
    [TestCase(35)]
    [TestCase(255)]
    [TestCase(256)]
    [TestCase(285)]
    public void PacketStatId_OutsideTheClientsTable_IsRefused(int id) {
        Assert.That(BasicAttributeIds.TryServerStat(id, out _), Is.False);
    }

    [TestCase(0, BasicAttribute.Strength)]
    [TestCase(29, BasicAttribute.BonusAtk)]
    [TestCase(33, BasicAttribute.Damage)]
    [TestCase(34, BasicAttribute.Unknown)]
    public void PacketStatId_InTheClientsTable(int id, BasicAttribute expected) {
        Assert.That(BasicAttributeIds.TryServerStat(id, out BasicAttribute type), Is.True);
        Assert.That(type, Is.EqualTo(expected));
    }

    // A kept basic option outside the table refuses the whole request; the bytes are still read. A special option's
    // id is not a basic stat id and is kept as it is.
    [TestCase(285)]
    [TestCase(256)]
    [TestCase(35)]
    public void Lock_OutsideTheClientsTable_RefusesTheLocks(int id) {
        var writer = new ByteWriter();
        writer.WriteByte(2);
        writer.WriteByte(1);
        writer.WriteShort((short) id);
        writer.WriteByte(0);
        writer.WriteShort((short) id);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(ItemRemakeRules.ReadLocks(reader), Is.Null);
        Assert.That(reader.Available, Is.EqualTo(0));

        var special = new ByteWriter();
        special.WriteByte(1);
        special.WriteByte(1);
        special.WriteShort((short) id);
        Assert.That(ItemRemakeRules.ReadLocks(new ByteReader(special.ToArray(), 0)), Is.EqualTo(new[] { new ItemRemakeRules.Lock(true, (short) id) }));
    }

    // A valid basic lock next to a bad one does not save the request; the last id, 34, is a stat.
    [Test]
    public void Lock_ValidNextToOutside_StillRefused_LastIdKept() {
        var writer = new ByteWriter();
        writer.WriteByte(2);
        writer.WriteByte(0);
        writer.WriteShort(34);
        writer.WriteByte(0);
        writer.WriteShort(35);
        Assert.That(ItemRemakeRules.ReadLocks(new ByteReader(writer.ToArray(), 0)), Is.Null);

        var last = new ByteWriter();
        last.WriteByte(1);
        last.WriteByte(0);
        last.WriteShort(34);
        Assert.That(ItemRemakeRules.ReadLocks(new ByteReader(last.ToArray(), 0)), Is.EqualTo(new[] { new ItemRemakeRules.Lock(false, (short) BasicAttribute.Unknown) }));
        Assert.That(BlackMarketHandler.ServerStatKey(1034), Is.EqualTo(1000 + (int) BasicAttribute.Unknown));
    }

    // A basic search key whose stat is outside the table is dropped (285 and 1285 are not bap 29 and 1029).
    [TestCase(35)]
    [TestCase(285)]
    [TestCase(1285)]
    [TestCase(1999)]
    public void BlackMarketKey_OutsideTheClientsTable_IsDropped(int key) {
        Assert.That(BlackMarketHandler.ServerStatKey(key), Is.Null);
    }
}

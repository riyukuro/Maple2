using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// ItemEnchant against CSceneStateGame::OnItemEnchant (scenestategame.cpp) and UIItemEnchantDialog
// (uiitemenchantdialog.cpp): u8 mode; 5 (OnEnchantInfoPacket :950): u16 type, long uid, u8 count x (int, int tag, int
// amount) (CIngredientContainer::Read, commonstruct_wa.cpp:255), u16 fail type, int count x (u16 stat, int value)
// (CEnchantOption::ReadOptions, unit_14058d5c0_enchantoption.cpp:23), type 1: 5 floats, 2 longs, u8, then for types 1
// and 2: int item, u16 rank, int count; 6 (FUN_1419c3950): long uid, int level; 7 (FUN_1419c3b90): int, int, int count x
// long, 5 floats; 9: none; 10: long uid, the item stream, the options; 11: long uid, the item stream, int, int, long,
// int fail type; 12: u16 error; 15: long uid, the item stream; 16: none. The item stream is read back against
// CItem::ReadFromStream in ItemLayoutTests, so it is compared here as the shared Item writer's bytes.
public class ItemEnchantLayoutTests {
    private static ByteReader Read(ByteWriter writer) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.ItemEnchant));
        return reader;
    }

    private static Item Gear() => new(TestGameSession.Metadata(11300001), 4, 1) { Uid = 8001 };

    private static Dictionary<BasicAttribute, BasicOption> Deltas() => new() { [BasicAttribute.Strength] = new BasicOption(7) };

    [Test]
    public void StageItem_Ophelia() {
        var catalysts = new List<IngredientInfo> { new(ItemTag.FameCompletionTicket, 3) };
        var rates = new EnchantRates { Success = 40, Fodder = 10, Charge = 5 };
        ByteReader reader = Read(ItemEnchantPacket.StageItem(EnchantType.Ophelia, Gear(), catalysts, Deltas(), rates, 2));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadShort(), Is.EqualTo((short) EnchantType.Ophelia));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(ItemTagMap.ToClient(ItemTag.FameCompletionTicket)));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadShort(), Is.EqualTo((short) EnchantFailType.None));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadShort(), Is.EqualTo((short) BasicAttribute.Strength.ClientStat()));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(new[] { reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat() },
            Is.EqualTo(new[] { 40f, 0f, 0f, 10f, 5f }));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(11300001));
        Assert.That(reader.ReadShort(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void StageItem_Peachy() {
        ByteReader reader = Read(ItemEnchantPacket.StageItem(EnchantType.Peachy, Gear(), new List<IngredientInfo>(), new Dictionary<BasicAttribute, BasicOption>(), new EnchantRates(), 0));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadShort(), Is.EqualTo((short) EnchantType.Peachy));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(new[] { reader.ReadInt(), reader.ReadShort(), reader.ReadInt() }, Is.EqualTo(new[] { 0, 0, 0 }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void ShortModes() {
        ByteReader reader = Read(ItemEnchantPacket.UpdateExp(8001, 55));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadInt(), Is.EqualTo(55));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemEnchantPacket.UpdateCharges(new List<long> { 5, 6 }, 12, 3, new EnchantRates { Success = 1, Fodder = 2, Charge = 3 }));
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 12, 3, 2 }));
        Assert.That(new[] { reader.ReadLong(), reader.ReadLong() }, Is.EqualTo(new long[] { 5, 6 }));
        Assert.That(new[] { reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat() },
            Is.EqualTo(new[] { 1f, 0f, 0f, 2f, 3f }));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemEnchantPacket.Refund());
        Assert.That(reader.ReadByte(), Is.EqualTo(9));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemEnchantPacket.Unknown16());
        Assert.That(reader.ReadByte(), Is.EqualTo(16));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void ResultModes() {
        // 10: uid, item stream, options (int count x (u16 stat, int value)).
        ByteReader reader = Read(ItemEnchantPacket.Success(Gear(), Deltas()));
        Assert.That(reader.ReadByte(), Is.EqualTo(10));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        byte[] rest = reader.ReadBytes(reader.Available);
        var stream = new ByteWriter();
        stream.WriteClass<Item>(Gear());
        byte[] item = stream.ToArray();
        Assert.That(rest[..item.Length], Is.EqualTo(item));
        var options = new ByteReader(rest[item.Length..], 0);
        Assert.That(options.ReadInt(), Is.EqualTo(1));
        Assert.That(options.ReadShort(), Is.EqualTo((short) BasicAttribute.Strength.ClientStat()));
        Assert.That(options.ReadInt(), Is.EqualTo(7));
        Assert.That(options.Available, Is.EqualTo(0));

        // 11: uid, item stream, int, int, long, int.
        reader = Read(ItemEnchantPacket.Failure(Gear(), 9));
        Assert.That(reader.ReadByte(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        rest = reader.ReadBytes(reader.Available);
        Assert.That(rest[..item.Length], Is.EqualTo(item));
        var tail = new ByteReader(rest[item.Length..], 0);
        Assert.That(new[] { tail.ReadInt(), tail.ReadInt() }, Is.EqualTo(new[] { 0, 0 }));
        Assert.That(tail.ReadLong(), Is.EqualTo(0));
        Assert.That(tail.ReadInt(), Is.EqualTo(9));
        Assert.That(tail.Available, Is.EqualTo(0));

        // 15: uid, item stream.
        reader = Read(ItemEnchantPacket.Unknown15(Gear()));
        Assert.That(reader.ReadByte(), Is.EqualTo(15));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadBytes(reader.Available), Is.EqualTo(item));
    }
}

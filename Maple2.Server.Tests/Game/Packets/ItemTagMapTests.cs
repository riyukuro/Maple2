using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The KMS 2525 client's tag table (CItemDataTable::InitItemTags, commonfiles/itemdatatable.cpp) has no
// DungeonRewardTypeA-D, so from 10 on its numbers are 4 lower than the server enum. Both locales run this client,
// so the numbers are the same under LOCALE=KR and LOCALE=NA.
public class ItemTagMapTests {
    [TestCase(ItemTag.None, 0)]
    [TestCase(ItemTag.FreeChannelChatCoupon, 1)]
    [TestCase(ItemTag.DungeonRewardTicketC, 9)]
    [TestCase(ItemTag.DungeonRewardTypeA, 0)]
    [TestCase(ItemTag.DungeonRewardTypeD, 0)]
    [TestCase(ItemTag.air_taxi, 10)]
    [TestCase(ItemTag.GreenCrystal, 94)]
    [TestCase(ItemTag.CrystalPiece, 96)]
    [TestCase(ItemTag.Onix, 97)]
    [TestCase(ItemTag.ChaosOnix, 98)]
    [TestCase(ItemTag.FishingLure, 100)]
    [TestCase(ItemTag.LockItemOptionWeapon, 101)]
    [TestCase(ItemTag.LockItemOptionArmor, 102)]
    [TestCase(ItemTag.WeddingHallCoupon_Grade3, 214)]
    [TestCase(ItemTag.GuildStorageItemOnlyToMe, 215)]
    [TestCase(ItemTag.PetEXP, 216)]
    [TestCase(ItemTag.UGCMapOX, 217)]
    [TestCase(ItemTag.Enix, 218)]
    [TestCase(ItemTag.Eragonix, 219)]
    [TestCase((ItemTag) 9999, 0)]
    [TestCase((ItemTag) (-1), 0)]
    public void ClientNumber(ItemTag tag, int expected) {
        Assert.That(ItemTagMap.ToClient(tag), Is.EqualTo(expected));
    }

    [Test]
    public void EveryClientNumberIsUsedOnce() {
        int[] numbers = System.Enum.GetValues<ItemTag>().Select(ItemTagMap.ToClient).Where(number => number != 0).ToArray();

        Assert.That(numbers, Is.Unique);
        Assert.That(numbers.Order(), Is.EqualTo(Enumerable.Range(1, 219)));
    }

    [Test]
    public void LimitBreakStageItemWritesClientTag() {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(11300001, "11300001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        var item = new Item(metadata, 4) { Uid = 91 };
        ByteWriter writer = LimitBreakPacket.StageItem(item, item, [new IngredientInfo(ItemTag.Onix, 7)]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.LimitBreak));
        reader.ReadByte();
        reader.ReadLong();
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(97));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
    }

    // The preview row's item id is 0: the client takes the tag's item (CIngredientInfo::GetItemId falls back to
    // CItemDataTable::GetTagItemId, filled from table/itemdata.xml <tag itemID>), which the KR data gives for all five
    // tags. A tag that maps to 0 here would drop its row from the dialog.
    [Test]
    public void LimitBreakCatalystTagsHaveClientNumbers() {
        ItemTag[] tags = [.. Maple2.Server.Core.Formulas.LimitBreak.GetCatalysts(0).Select(ingredient => ingredient.Tag), ItemTag.CrystalPiece];

        Assert.That(tags.Select(ItemTagMap.ToClient), Is.EqualTo(new[] { 98, 97, 210, 211, 96 }));
    }

    [Test]
    public void ItemExchangeScrollSetTargetWritesClientTag() {
        ByteWriter writer = ItemExchangeScrollPacket.SetTarget(5, 6, [new ItemComponent(0, -1, 3, ItemTag.ChaosOnix)]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ItemExchange));
        reader.ReadByte();
        Assert.That(reader.ReadLong(), Is.EqualTo(5));
        Assert.That(reader.ReadLong(), Is.EqualTo(6));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(98));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
    }
}

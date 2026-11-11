using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// Sockets of a new item: the item's own socketDataId for any equipment slot, a drop row's socketDataID in its place,
// and the SkinSocket feature gate for outfits. Made-up item ids; socket rows shaped like table/itemsocket.xml.
public class ItemSocketCreateTests {
    private static readonly ItemSocketTable Table = new(new Dictionary<int, IReadOnlyDictionary<int, ItemSocketMetadata>> {
        [1002] = new Dictionary<int, ItemSocketMetadata> { [0] = new(2, 2) },
        [1005] = new Dictionary<int, ItemSocketMetadata> { [0] = new(3, 1) },
        [9005] = new Dictionary<int, ItemSocketMetadata> { [0] = new(4, 0) },
        [2] = new Dictionary<int, ItemSocketMetadata> { [0] = new(0, 0), [4] = new(3, 0) },
    });

    private static ItemMetadata Item(int id, int type = 1, int skinType = 0, int socketId = 0, int level = 1) {
        var limit = new ItemMetadataLimit(Gender.All, level, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var property = new ItemMetadataProperty(false, skinType, 1, type, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, socketId, false, [], [], [], 0, 0);
        return new ItemMetadata(id, id.ToString(), [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
    }

    private static ItemSocket? Create(ItemMetadata item, int rarity = 1, int socketId = 0) {
        return ItemStatsCalculator.CreateSockets(item, new ItemType(item.Id), rarity, Table, socketId);
    }

    [Test]
    public void OwnSocketId_AnySlot() {
        // A hat outfit (slot 13) with socketDataId 9005: 4 sockets, none open.
        ItemSocket? hat = Create(Item(11300001, skinType: 4, socketId: 9005));
        Assert.That(hat, Is.Not.Null);
        Assert.That((hat!.MaxSlots, hat.UnlockSlots), Is.EqualTo(((byte) 4, (byte) 0)));

        ItemSocket? top = Create(Item(11400001, socketId: 1002));
        Assert.That((top!.MaxSlots, top.UnlockSlots), Is.EqualTo(((byte) 2, (byte) 2)));
    }

    [Test]
    public void OwnSocketId_RankRowThenAnyRank() {
        Assert.That(Create(Item(11200001, socketId: 2), rarity: 4)!.MaxSlots, Is.EqualTo((byte) 3));
        Assert.That(Create(Item(11200001, socketId: 2), rarity: 2)!.MaxSlots, Is.EqualTo((byte) 0));
        Assert.That(Create(Item(11200001, socketId: 7777)), Is.Null);
    }

    [Test]
    public void NotEquipment_NoSockets() {
        // CItem::FUN_14052b420 fills sockets only for item type 1.
        Assert.That(Create(Item(11300001, type: 2, socketId: 1002)), Is.Null);
        Assert.That(Create(Item(40100001, type: 21, socketId: 1002)), Is.Null);
    }

    [Test]
    public void NoSocketId_ArmorHasNone() {
        Assert.That(Create(Item(11300001, level: 60), rarity: 4), Is.Null);
    }

    [Test]
    public void NoSocketId_AccessoryUsesLevelLimit() {
        // calcItemSocketMaxCount reads the level limit (CItemSocketTable::GetMaxSocketCount passes limit.levelLimit).
        Assert.That(Create(Item(11900001, level: 50), rarity: 4)!.MaxSlots, Is.EqualTo((byte) 3));
        Assert.That(Create(Item(11900001, level: 49), rarity: 4)!.MaxSlots, Is.EqualTo((byte) 0));
        Assert.That(Create(Item(12000001, level: 60), rarity: 3)!.MaxSlots, Is.EqualTo((byte) 1));
        // An outfit's skin type keeps the Lua count at 0.
        Assert.That(Create(Item(11200001, skinType: 4, level: 60), rarity: 4)!.MaxSlots, Is.EqualTo((byte) 0));
    }

    [Test]
    public void DropSocketId_ReplacesOwn() {
        // A necklace without a socket id dropped from a row with socketDataID 1005: 3 sockets, 1 open.
        ItemSocket? necklace = Create(Item(11900001, level: 60), rarity: 4, socketId: 1005);
        Assert.That((necklace!.MaxSlots, necklace.UnlockSlots), Is.EqualTo(((byte) 3, (byte) 1)));
        Assert.That(Create(Item(11400001, socketId: 1002), socketId: 9005)!.MaxSlots, Is.EqualTo((byte) 4));
    }

    [Test]
    public void DropSocketId_GatedOutfitKeepsNone() {
        // The ingest cleared this outfit's socket id (SkinSocket off), so the drop row's id is not used either.
        Assert.That(Create(Item(11300001, skinType: 4), socketId: 1002), Is.Null);
        Assert.That(Create(Item(11300001, skinType: 4, socketId: 9005), socketId: 1002)!.MaxSlots, Is.EqualTo((byte) 2));
    }

    [TestCase(9005, 4, true, 9005)]
    [TestCase(9005, 4, false, 0)]
    [TestCase(1003, 0, false, 1003)]
    [TestCase(0, 4, true, 0)]
    public void SkinSocketGate(int socketDataId, int skinType, bool enabled, int expected) {
        Assert.That(ItemSocketRules.SocketId(socketDataId, skinType, enabled), Is.EqualTo(expected));
    }
}

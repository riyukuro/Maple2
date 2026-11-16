using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;

namespace Maple2.Server.Tests.Game.Manager;

// RoomDungeon mode 8, one more reward count bought with the room's coupon tickets (DungeonManager.AddExtraRewardCount):
// the refusals of the client's errors, each one RoomDungeon packet and no count added. The room and its ticket row are
// made up.
public class DungeonExtraRewardRefusalTests {
    private const int Room = 23050301;
    private TestGameSession test = null!;

    private void Start(DungeonExtraRewardTicket? ticket) {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 50000016) with { ExtraRewardTicket = ticket };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.Start();
        test.TakeSent();
    }

    private static DungeonExtraRewardTicket Ticket(int max) => new(1, 1, ItemTag.FreeChannelChatCoupon, 0, max, [1, 2, 3], false);

    private void Buy(int dungeonId) => test.Session.Dungeon.AddExtraRewardCount(dungeonId);

    private SendOp[] Ops() => test.TakeSent().Select(TestGameSession.Op).ToArray();

    [Test]
    public void AnUnknownRoom_TheExtraRewardError() {
        Start(Ticket(3));
        Buy(99999999);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.RoomDungeon }));
    }

    [Test]
    public void ARoomWithoutATicket_TheExtraRewardError() {
        Start(null);
        Buy(Room);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.RoomDungeon }));
        Assert.That(test.Session.Dungeon.Records[Room].ExtraSubClears, Is.EqualTo(0));
    }

    [Test]
    public void AtTheMaximumBought_TheMaxCountError() {
        Start(Ticket(2));
        test.Session.Dungeon.Records[Room].ExtraSubClears = 2;
        test.Session.Dungeon.Records[Room].UnionCooldownTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        Buy(Room);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.RoomDungeon }));
        Assert.That(test.Session.Dungeon.Records[Room].ExtraSubClears, Is.EqualTo(2));
    }

    [Test]
    public void WithoutTheTicketItems_TheLackError() {
        Start(Ticket(3));
        Buy(Room);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.RoomDungeon }));
        Assert.That(test.Session.Dungeon.Records[Room].ExtraSubClears, Is.EqualTo(0));
    }

    // With the ticket items the count is bought: the tickets of the next count are taken (the first count costs 1 here),
    // the record's extra clears go up and the record and the add notice are sent.
    [Test]
    public void WithTheTicketItems_TheCountIsBought() {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 50000016) with { ExtraRewardTicket = Ticket(3) };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        ItemMetadata metadata = TestGameSession.Metadata(20000123, slotMax: 99);
        test.Store(metadata with { Property = metadata.Property with { Tag = ItemTag.FreeChannelChatCoupon } }, amount: 2);
        test.Start();
        test.TakeSent();
        int tickets() => test.Session.Item.Inventory.Find(ItemTag.FreeChannelChatCoupon).Sum(item => item.Amount);

        Buy(Room);

        Assert.Multiple(() => {
            Assert.That(test.Session.Dungeon.Records[Room].ExtraSubClears, Is.EqualTo(1));
            Assert.That(tickets(), Is.EqualTo(1));
            Assert.That(Ops().Count(op => op == SendOp.RoomDungeon), Is.EqualTo(2));
        });
    }
}

using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A channel line takes a free channel chat coupon that has not expired; an expired one is no coupon (as for the world
// line), so the line costs merets instead of being refused when Consume will not take it.
public class ChannelChatCouponTests {
    private static Item Coupon(TestGameSession test, int id, long expiryTime) {
        ItemMetadata metadata = TestGameSession.Metadata(id, type: 4);
        metadata = metadata with { Property = metadata.Property with { Tag = ItemTag.FreeChannelChatCoupon } };
        Item item = test.Store(metadata);
        item.ExpiryTime = expiryTime;
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.SaveItems(test.CharacterId, item), Is.True);
        return item;
    }

    [Test]
    public void ExpiredCoupon_IsNoCoupon() {
        var test = new TestGameSession();
        Coupon(test, 20300080, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60);
        GameSession session = test.Start();
        Assume.That(session.Item.Inventory.Filter(item => item.Metadata.Property.Tag == ItemTag.FreeChannelChatCoupon).Count(), Is.EqualTo(1));

        Assert.That(UserChatHandler.ChannelChatCoupon(session), Is.Null);
    }

    [Test]
    public void LiveCoupon_IsTakenOverAnExpiredOne() {
        var test = new TestGameSession();
        Coupon(test, 20302776, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60);
        Item live = Coupon(test, 20300039, 0);
        GameSession session = test.Start();

        Assert.That(UserChatHandler.ChannelChatCoupon(session)?.Uid, Is.EqualTo(live.Uid));
    }
}

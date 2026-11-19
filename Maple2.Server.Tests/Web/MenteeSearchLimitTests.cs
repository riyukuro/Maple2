using System;
using System.Net;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Controllers;
using Maple2.Server.Web.Packet;

namespace Maple2.Server.Tests.Web;

// ruq.aspx carries no token, so each address (an IPv6 /56) gets a few mentee searches a minute; a refused one gets the
// client's failed-search answer (result 1, no list). The table holds at most DefaultMaxTracked addresses.
public class MenteeSearchLimitTests {
    private const long Minute = 60_000;

    private static AddressRateLimiter Limiter() => new(WebController.MenteeSearchLimit, WebController.MenteeSearchWindow);

    [Test]
    public void AnAddress_GetsTheLimitThenIsRefusedUntilItsWindowEnds() {
        AddressRateLimiter limiter = Limiter();
        for (int i = 0; i < WebController.MenteeSearchLimit; i++) {
            Assert.That(limiter.TryAcquire("198.51.100.7", 1000 + i), Is.True, $"search {i + 1}");
        }
        Assert.That(limiter.TryAcquire("198.51.100.7", 2000), Is.False);
        Assert.That(limiter.TryAcquire("198.51.100.7", 1000 + Minute - 1), Is.False);
        Assert.That(limiter.TryAcquire("198.51.100.7", 1000 + Minute), Is.True);
    }

    [Test]
    public void ARefusedSearch_DoesNotPushTheWindowBack() {
        AddressRateLimiter limiter = Limiter();
        for (int i = 0; i < WebController.MenteeSearchLimit; i++) {
            limiter.TryAcquire("198.51.100.7", 0);
        }
        for (int i = 1; i < 50; i++) {
            Assert.That(limiter.TryAcquire("198.51.100.7", i * 1000), Is.False);
        }
        Assert.That(limiter.TryAcquire("198.51.100.7", Minute), Is.True);
    }

    [Test]
    public void OtherAddresses_HaveTheirOwnCount() {
        AddressRateLimiter limiter = Limiter();
        for (int i = 0; i < WebController.MenteeSearchLimit; i++) {
            limiter.TryAcquire("198.51.100.7", 0);
        }
        Assert.That(limiter.TryAcquire("198.51.100.7", 1), Is.False);
        Assert.That(limiter.TryAcquire("198.51.100.8", 1), Is.True);
    }

    [Test]
    public void Keys_FoldMappedIPv4AndIPv6Homes() {
        Assert.That(AddressRateLimiter.Key(IPAddress.Parse("::ffff:198.51.100.7")), Is.EqualTo("198.51.100.7"));
        Assert.That(AddressRateLimiter.Key(IPAddress.Parse("2001:db8:1:2:aaaa::1")), Is.EqualTo("2001:db8:1::/56"));
        Assert.That(AddressRateLimiter.Key(IPAddress.Parse("2001:db8:1:ff:bbbb::2")), Is.EqualTo("2001:db8:1::/56"),
            "every /64 of one /56 is one home");
        Assert.That(AddressRateLimiter.Key(IPAddress.Parse("2001:db8:1:100::1")),
            Is.Not.EqualTo(AddressRateLimiter.Key(IPAddress.Parse("2001:db8:1:2::1"))));
        Assert.That(AddressRateLimiter.Key(null), Is.EqualTo(""));
    }

    [Test]
    public void ExpiredAddresses_AreDroppedAndTheTableShrinks() {
        AddressRateLimiter limiter = Limiter();
        for (int i = 0; i < 5000; i++) {
            limiter.TryAcquire($"10.0.{i / 256}.{i % 256}", 0);
        }
        Assert.That(limiter.Tracked, Is.EqualTo(5000), "none expired yet");
        Assert.That(limiter.Capacity, Is.GreaterThanOrEqualTo(5000));
        Assert.That(limiter.TryAcquire("198.51.100.7", 2 * Minute), Is.True);
        Assert.That(limiter.Tracked, Is.EqualTo(1));
        Assert.That(limiter.Capacity, Is.LessThan(100), "trimmed after the burst");
    }

    [Test]
    public void AFullTable_DropsTheOldestWindow() {
        var limiter = new AddressRateLimiter(WebController.MenteeSearchLimit, WebController.MenteeSearchWindow, maxTracked: 3);
        for (int i = 0; i < WebController.MenteeSearchLimit; i++) {
            limiter.TryAcquire("198.51.100.1", 0);
        }
        Assert.That(limiter.TryAcquire("198.51.100.1", 1), Is.False);
        for (int i = 0; i < WebController.MenteeSearchLimit; i++) {
            limiter.TryAcquire("198.51.100.2", 1);
        }
        limiter.TryAcquire("198.51.100.3", 2);
        Assert.That(limiter.TryAcquire("198.51.100.4", 3), Is.True, "a new address is never refused for room");
        Assert.That(limiter.Tracked, Is.EqualTo(3));
        Assert.That(limiter.TryAcquire("198.51.100.2", 4), Is.False, "a newer window is kept");
        Assert.That(limiter.TryAcquire("198.51.100.1", 5), Is.True, "the oldest window was dropped and starts over");
        Assert.That(limiter.Tracked, Is.EqualTo(3));
    }

    [Test]
    public void TheDefaultCap_BoundsTheTable() {
        AddressRateLimiter limiter = Limiter();
        for (int i = 0; i < AddressRateLimiter.DefaultMaxTracked + 500; i++) {
            Assert.That(limiter.TryAcquire($"10.{i >> 16}.{(i >> 8) & 255}.{i & 255}", i / 100), Is.True);
        }
        Assert.That(limiter.Tracked, Is.EqualTo(AddressRateLimiter.DefaultMaxTracked));
    }

    [Test]
    public void TheRefusal_IsAFailedSearchWithNoList() {
        var reader = new ByteReader(MentorPacket.MenteeListRefused().ToArray(), 0);
        Assert.That(reader.ReadInt(), Is.Not.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

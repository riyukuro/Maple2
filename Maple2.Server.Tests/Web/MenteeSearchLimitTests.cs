using System;
using System.Net;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Controllers;
using Maple2.Server.Web.Packet;

namespace Maple2.Server.Tests.Web;

// ruq.aspx carries no token, so each address gets a few mentee searches a minute; a refused one gets the client's
// failed-search answer (result 1, no list).
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
    public void Keys_FoldMappedIPv4AndIPv6Hosts() {
        Assert.That(AddressRateLimiter.Key(IPAddress.Parse("::ffff:198.51.100.7")), Is.EqualTo("198.51.100.7"));
        Assert.That(AddressRateLimiter.Key(IPAddress.Parse("2001:db8:1:2:aaaa::1")),
            Is.EqualTo(AddressRateLimiter.Key(IPAddress.Parse("2001:db8:1:2:bbbb::2"))));
        Assert.That(AddressRateLimiter.Key(IPAddress.Parse("2001:db8:1:2::1")),
            Is.Not.EqualTo(AddressRateLimiter.Key(IPAddress.Parse("2001:db8:1:3::1"))));
        Assert.That(AddressRateLimiter.Key(null), Is.EqualTo(""));
    }

    [Test]
    public void ExpiredAddresses_AreDropped() {
        AddressRateLimiter limiter = Limiter();
        for (int i = 0; i < 5000; i++) {
            limiter.TryAcquire($"10.0.{i / 256}.{i % 256}", 0);
        }
        Assert.That(limiter.Tracked, Is.EqualTo(5000), "none expired yet");
        limiter.TryAcquire("198.51.100.7", 2 * Minute);
        for (int i = 0; i < 4000; i++) {
            limiter.TryAcquire($"10.1.{i / 256}.{i % 256}", 2 * Minute);
        }
        Assert.That(limiter.Tracked, Is.LessThan(5000));
    }

    [Test]
    public void TheRefusal_IsAFailedSearchWithNoList() {
        var reader = new ByteReader(MentorPacket.MenteeListRefused().ToArray(), 0);
        Assert.That(reader.ReadInt(), Is.Not.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

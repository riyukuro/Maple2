using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class HongBaoRulesTests {
    // ItemBillboard mode 1: i32 envelope id after the mode byte.
    [Test]
    public void ClaimBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(0x01020304);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(HongBaoRules.ReadClaim(reader), Is.EqualTo(0x01020304));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void OpCodes() {
        Assert.That((int) RecvOp.ItemBillboard, Is.EqualTo(0x009E));
        Assert.That((int) SendOp.PlayerHost, Is.EqualTo(0x0105));
    }

    [TestCase(true, false, false, 1, 5, HongBaoRules.ClaimStep.Give)]
    [TestCase(true, false, false, 4, 5, HongBaoRules.ClaimStep.Give)]
    [TestCase(true, false, false, 5, 5, HongBaoRules.ClaimStep.Refuse)]
    [TestCase(true, false, false, 6, 5, HongBaoRules.ClaimStep.Refuse)]
    [TestCase(true, false, true, 1, 5, HongBaoRules.ClaimStep.Refuse)]
    [TestCase(false, false, false, 0, 0, HongBaoRules.ClaimStep.Refuse)]
    [TestCase(false, false, false, 0, 5, HongBaoRules.ClaimStep.Refuse)]
    [TestCase(true, true, false, 1, 5, HongBaoRules.ClaimStep.Refuse)]
    public void Claim(bool found, bool expired, bool alreadyClaimed, int claimedUsers, int maxUsers, HongBaoRules.ClaimStep expected) {
        Assert.That(HongBaoRules.Claim(found, expired, alreadyClaimed, claimedUsers, maxUsers), Is.EqualTo(expected));
    }

    // PlayerHost mode 2, given: u8 2, bool 1, i32 envelope item, i32 reward item, i32 count, opener, receiver.
    [Test]
    public void GiftLayout() {
        ByteWriter writer = PlayerHostPacket.GiftHongBao(20300001, 90000004, 7, "opener", "me");
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.PlayerHost));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(20300001));
        Assert.That(reader.ReadInt(), Is.EqualTo(90000004));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("opener"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("me"));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // PlayerHost mode 2, refused: u8 2, bool 0 and nothing else.
    [Test]
    public void RefuseLayout() {
        ByteWriter writer = PlayerHostPacket.InactiveHongBao();
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.PlayerHost));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // A character has one share, whatever object id it has in the field: leaving and coming back
    // gives no second one.
    [Test]
    public void OneSharePerCharacter() {
        var shares = new HongBaoShares([5, 3, 2], 3);
        Assert.That(shares.TryTake(1001, out int first), Is.True);
        Assert.That(first, Is.EqualTo(5));
        Assert.That(shares.TryTake(1001, out int again), Is.False);
        Assert.That(again, Is.EqualTo(0));
        Assert.That(shares.Count, Is.EqualTo(1));
        Assert.That(shares.TryTake(1002, out int second), Is.True);
        Assert.That(second, Is.EqualTo(3));
    }

    [Test]
    public void NoShareLeft() {
        var shares = new HongBaoShares([4, 1], 2);
        Assert.That(shares.TryTake(1, out _), Is.True);
        Assert.That(shares.TryTake(2, out _), Is.True);
        Assert.That(shares.TryTake(3, out int amount), Is.False);
        Assert.That(amount, Is.EqualTo(0));
        Assert.That(shares.Count, Is.EqualTo(2));
    }

    // Fewer amounts than users: the amounts end the envelope.
    [Test]
    public void ShortAmounts() {
        var shares = new HongBaoShares([4], 3);
        Assert.That(shares.TryTake(1, out _), Is.True);
        Assert.That(shares.TryTake(2, out _), Is.False);
        Assert.That(shares.Count, Is.EqualTo(1));
    }

    // Claims at the same time: each share goes out once and nobody past the last share is entered.
    [Test]
    public void ParallelClaims() {
        for (int round = 0; round < 50; round++) {
            int[] amounts = Enumerable.Range(1, 6).ToArray();
            var shares = new HongBaoShares(amounts, 6);
            var given = new ConcurrentBag<int>();
            Parallel.For(0, 64, i => {
                // Two tries for each of 32 characters.
                if (shares.TryTake(5000 + i / 2, out int amount)) {
                    given.Add(amount);
                }
            });
            Assert.That(given.OrderBy(amount => amount), Is.EqualTo(amounts));
            Assert.That(shares.Count, Is.EqualTo(6));
        }
    }

    // An envelope of 3 items for 6 users gives 3 shares of 1: no share is 0 or negative, and no more is given than it
    // holds. The KR envelopes (50 or 500 for 6) each give 6 shares of at least 1 that add up to the count.
    [TestCase(3, 6, 3)]
    [TestCase(1, 6, 1)]
    [TestCase(50, 6, 6)]
    [TestCase(500, 6, 6)]
    public void Distributions_GiveEachShareAtLeastOne(int itemCount, int totalUser, int shares) {
        for (int i = 0; i < 200; i++) {
            var hongBao = new Maple2.Server.Game.Model.HongBao(null!, null!, 20300964, 1, 90000020, totalUser, 0, 300, itemCount);

            Assert.That(hongBao.MaxUserCount, Is.EqualTo(shares));
            Assert.That(hongBao.Distributions, Has.Length.EqualTo(shares));
            Assert.That(hongBao.Distributions, Has.All.GreaterThanOrEqualTo(1));
            Assert.That(hongBao.Distributions.Sum(), Is.EqualTo(itemCount));
        }
    }
}

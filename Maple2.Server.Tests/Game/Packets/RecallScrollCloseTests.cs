using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// RecallScroll mode 0: the client (CSceneStateGame, RecallScroll) reads the type byte and closes that type's notice.
public class RecallScrollCloseTests {
    private static PendingRecall Recall(long callerId, long endTime) => new(callerId, "caller", 2000001, endTime);

    [Test]
    public void CloseIsModeZeroThenType() {
        var reader = new ByteReader(RecallScrollPacket.Close(RecallScrollType.Guild).ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.RecallScroll));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) RecallScrollType.Guild));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void OfferReportsAReplacedRecall() {
        var recalls = new PendingRecall?[3];
        Assert.That(RecallSlots.Offer(recalls, RecallScrollType.Party, Recall(1, 100)), Is.False);
        Assert.That(RecallSlots.Offer(recalls, RecallScrollType.Party, Recall(2, 200)), Is.True);
        Assert.That(recalls[(int) RecallScrollType.Party]!.CallerId, Is.EqualTo(2));
        Assert.That(RecallSlots.Offer(recalls, RecallScrollType.Guild, Recall(3, 200)), Is.False);
    }

    [Test]
    public void TakeMatchesTheCallerWhenGiven() {
        var recalls = new PendingRecall?[3];
        RecallSlots.Offer(recalls, RecallScrollType.Guild, Recall(7, 100));
        Assert.That(RecallSlots.Take(recalls, RecallScrollType.Guild, 8), Is.False);
        Assert.That(RecallSlots.Take(recalls, RecallScrollType.Party), Is.False);
        Assert.That(RecallSlots.Take(recalls, RecallScrollType.Guild, 7), Is.True);
        Assert.That(recalls[(int) RecallScrollType.Guild], Is.Null);
        Assert.That(RecallSlots.Take(recalls, RecallScrollType.Guild), Is.False);

        RecallSlots.Offer(recalls, RecallScrollType.Wedding, Recall(9, 100));
        Assert.That(RecallSlots.Take(recalls, RecallScrollType.Wedding), Is.True);
    }

    [Test]
    public void TakeExpiredDropsOnlyRecallsPastTheirEnd() {
        var recalls = new PendingRecall?[3];
        RecallSlots.Offer(recalls, RecallScrollType.Party, Recall(1, 99));
        RecallSlots.Offer(recalls, RecallScrollType.Guild, Recall(2, 100));
        RecallSlots.Offer(recalls, RecallScrollType.Wedding, Recall(3, 101));

        // The answer is still taken at the end time itself (RecallScrollHandler refuses EndTime < now).
        Assert.That(RecallSlots.TakeExpired(recalls, 100), Is.EqualTo(new[] { RecallScrollType.Party }));
        Assert.That(RecallSlots.TakeExpired(recalls, 101), Is.EqualTo(new[] { RecallScrollType.Guild }));
        Assert.That(recalls[(int) RecallScrollType.Wedding], Is.Not.Null);
        Assert.That(RecallSlots.TakeExpired(recalls, 101), Is.Empty);
    }
}

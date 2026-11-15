using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A token gain made inside a store (AddUnsent) is sent after it (NotifyAdded) as Change sends a gain: with the part
// over the cap as the packet's overflow, which the client shows its over-cap message for (scenestategame.cpp:7115).
public class CurrencyAddUnsentTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.TakeSent();
    }

    // At the cap: nothing is added, and the gain still sends its packet with the whole gain as overflow.
    [Test]
    public void AtTheCap_SendsTheOverflow() {
        long max = session.ServerTableMetadata.ConstantsTable.LuTokenMax;
        session.Player.Value.Currency.Rue = max;

        CurrencyManager.Added added = session.Currency.AddUnsent(CurrencyType.Rue, 100);
        Assert.That((added.Amount, added.Overflow), Is.EqualTo((0L, 100L)));
        Assert.That(test.TakeSent(), Is.Empty);

        session.Currency.NotifyAdded(added);
        Assert.That(test.TakeSent(), Is.EqualTo(new[] {
            CurrencyPacket.UpdateCurrency(session.Player.Value.Currency, CurrencyType.Rue, 0, 100).ToArray(),
        }));
    }

    // Across the cap: the part under it is added, the rest is the overflow; Change sends the same packet.
    [Test]
    public void AcrossTheCap_AsChange() {
        long max = session.ServerTableMetadata.ConstantsTable.LuTokenMax;
        session.Player.Value.Currency.Rue = max - 30;
        CurrencyManager.Added added = session.Currency.AddUnsent(CurrencyType.Rue, 100);
        Assert.That((added.Amount, added.Overflow), Is.EqualTo((30L, 70L)));
        session.Currency.NotifyAdded(added);
        List<byte[]> notified = test.TakeSent();

        session.Player.Value.Currency.Rue = max - 30;
        session.Currency.Change(CurrencyType.Rue, 100);
        Assert.That(notified, Is.EqualTo(test.TakeSent()));
        Assert.That(session.Player.Value.Currency.Rue, Is.EqualTo(max));
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The merets, the other wallets and the fame points are changed by the packet thread and by other threads (the
// field's: rank duel, trigger and dungeon rewards, quests completed by a condition update; another session's: a
// wedding hall paid by the partner). Each change is read and written in one step, so none is lost. Before, two
// changes that read the same balance wrote over each other.
public class WalletChangeThreadsTests {
    private const int Count = 20000;

    private static void TwoThreads(Action change) {
        using var start = new Barrier(2);
        void Run() {
            start.SignalAndWait();
            for (int i = 0; i < Count; i++) {
                change();
            }
        }
        var other = new Thread(Run);
        other.Start();
        Run();
        other.Join();
    }

    [Test]
    public void Merets_NoChangeLost() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        TwoThreads(() => session.Currency.ChangeMeret(1));
        Assert.That(session.Currency.Meret, Is.EqualTo(2L * Count));
    }

    [Test]
    public void GameMerets_NoChangeLost() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        TwoThreads(() => session.Currency.ChangeGameMeret(1));
        Assert.That(session.Currency.GameMeret, Is.EqualTo(2L * Count));
    }

    [Test]
    public void Wallet_NoChangeLost() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        TwoThreads(() => session.Currency.Change(CurrencyType.StarPoint, 1));
        Assert.That(session.Currency[CurrencyType.StarPoint], Is.EqualTo(2L * Count));
    }

    [Test]
    public void FamePoints_NoGainLost() {
        var test = new TestGameSession();
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        GameSession session = test.Start();
        TwoThreads(() => session.Quest.AddFamePoints(AllianceType.GreenHoods, 1));
        Assert.That(session.Player.Value.Unlock.FamePoints[AllianceType.GreenHoods], Is.EqualTo(2 * Count));
    }

    // A take made without its packet (the wedding hall's, before its save) sends nothing; its give-back sends the
    // balance, and NotifyMeretTaken sends the take's change.
    [Test]
    public void QuietMeretTake_PacketsAfterward() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Currency.Meret = 100;
        test.TakeSent();

        Assert.That(session.Currency.TryTakeMeret(40, notify: false), Is.True);
        List<byte[]> quiet = test.TakeSent();
        session.Currency.ReturnMeret(40);
        List<byte[]> back = test.TakeSent();
        Assert.That(session.Currency.TryTakeMeret(30, notify: false), Is.True);
        session.Currency.NotifyMeretTaken(30);
        List<byte[]> taken = test.TakeSent();

        Assert.Multiple(() => {
            Assert.That(quiet, Is.Empty);
            Assert.That(back.ConvertAll(p => BitConverter.ToUInt16(p, 0)), Is.EqualTo(new[] { (ushort) SendOp.Meret }));
            Assert.That(BitConverter.ToInt64(back[0], 34), Is.EqualTo(0));
            Assert.That(BitConverter.ToInt64(taken[0], 2), Is.EqualTo(70));
            Assert.That(BitConverter.ToInt64(taken[0], 34), Is.EqualTo(-30));
            Assert.That(session.Currency.Meret, Is.EqualTo(70));
        });
    }
}

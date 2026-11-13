using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The merets, the other wallets and the fame points are changed by the packet thread and by other threads (the
// field's: rank duel, trigger and dungeon rewards, quests completed by a condition update; another session's: a
// wedding hall paid by the partner). Each change is read and written in one step, so none is lost. Before, two
// changes that read the same balance wrote over each other.
public class WalletChangeThreadsTests {
    private const int Count = 20000;

    private static void TwoThreads(Action change) => TwoThreads(change, change);

    private static void TwoThreads(Action mine, Action others) {
        using var start = new Barrier(2);
        void Run(Action change) {
            start.SignalAndWait();
            for (int i = 0; i < Count; i++) {
                change();
            }
        }
        var other = new Thread(() => Run(others));
        other.Start();
        Run(mine);
        other.Join();
    }

    // A wallet's packet is sent in the change's step, so the packets show the balances in the order they were
    // reached and the last one shows the final balance. Before, a packet went after the lock and a later
    // balance could be shown before an earlier one.
    [Test]
    public void TokenPackets_InBalanceOrder() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();
        TwoThreads(() => session.Currency.Change(CurrencyType.StarPoint, 1));
        List<long> shown = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.CurrencyToken)
            .Select(packet => BitConverter.ToInt64(packet, 3)).ToList();
        Assert.That(shown, Is.EqualTo(Enumerable.Range(1, 2 * Count).Select(value => (long) value)));
    }

    // A payer's check of its merets can be out of date at the take: another thread (a wedding hall paid by the
    // partner) can take them between the two. The style slot extend then refuses as a lack of merets; before,
    // its take threw.
    [Test]
    public void StyleSlotExtend_AgainstAnotherTake_RefusedNotThrown() {
        var test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            BeautyStyleExpandSlotPrice = 980, BeautyStyleExpandSlotCount1time = 3, BeautyStyleMaxSlotCount = 6,
        });
        GameSession session = test.Start();
        session.Beauty = new BeautyManager(session);
        session.Currency.Meret = 980;
        int thrown = 0;
        int extended = 0;
        TwoThreads(() => {
            try {
                if (session.Beauty.ExtendSlots()) {
                    extended++;
                    session.Player.Value.Unlock.HairSlotExpand = 0;
                    session.Currency.ReturnMeret(980);
                }
            } catch (ArgumentException) {
                thrown++;
            }
        }, () => {
            if (session.Currency.TryTakeMeret(980, notify: false)) {
                session.Currency.ReturnMeret(980);
            }
        });
        Assert.Multiple(() => {
            Assert.That(thrown, Is.Zero);
            Assert.That(extended, Is.Positive);
            Assert.That(session.Currency.Meret, Is.EqualTo(980));
        });
    }

    // The build budget takes a raise of its merets, then its mesos. Another thread (a field's tax, a trade) can take
    // the mesos after the budget's check: the raise is then refused and its merets given back. Before, the meso
    // take threw and the merets taken before it were kept.
    [Test]
    public void BuildBudget_AgainstAnotherMesoTake_KeepsNoMerets() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        session.Currency.Meso = 1000;
        session.Currency.Meret = 1000;
        int thrown = 0;
        int raised = 0;
        bool done = false;
        // The other thread takes and gives back the mesos for as long as the budget is changed.
        var other = new Thread(() => {
            while (!Volatile.Read(ref done)) {
                if (session.Currency.TryTakeMeso(600, notify: false)) {
                    session.Currency.ReturnMeso(600);
                }
            }
        });
        other.Start();
        for (int i = 0; i < 3000; i++) {
            try {
                session.Housing.SetBuildBudget(600, 600);
            } catch (ArgumentException) {
                thrown++;
            }
            if (session.Player.Value.Home.BuildMeso == 600) {
                raised++;
            }
            session.Housing.SetBuildBudget(0, 0);
            // Each give-back sends a meso packet; the queue is emptied so it does not grow without bound.
            if (i % 100 == 0) {
                test.TakeSent();
            }
        }
        Volatile.Write(ref done, true);
        other.Join();
        Home home = session.Player.Value.Home;
        Assert.Multiple(() => {
            Assert.That(thrown, Is.Zero);
            Assert.That(raised, Is.Positive);
            Assert.That(session.Currency.Meso + home.BuildMeso, Is.EqualTo(1000));
            Assert.That(session.Currency.Meret + home.BuildMerat, Is.EqualTo(1000));
        });
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

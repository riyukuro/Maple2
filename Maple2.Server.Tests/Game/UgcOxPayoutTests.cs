using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The payments of a home OX quiz event at its close (FieldManager.Factory.CloseUgcOx): a payee standing in the
// stadium gets the mesos and the client's notice there; anyone else gets a mail with the mesos. The event's host,
// entrants and fee are made up.
public class UgcOxPayoutTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long HostAccount = 900;
    private const long Host = 901;
    private const long Fee = 1000;

    private TestGameSession test = null!;
    private FieldManager.Factory factory = null!;
    private long Other => test.CharacterId + 1;

    [SetUp]
    public void Stadium() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(UgcOxRules.StadiumMapId));
        factory = (FieldManager.Factory) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager.Factory));
        void Set(string name, object? value) => typeof(FieldManager.Factory).GetField(name, Any)!.SetValue(factory, value);
        Set("logger", Serilog.Log.Logger);
        Set("ugcOxEvents", new ConcurrentDictionary<long, UgcOxEvent>());
        Set("homes", new ConcurrentDictionary<long, HomeFieldManager>());
        Set("<GameStorage>k__BackingField", test.Storage);
        test.TakeSent();
    }

    // An event the session and one other character entered, each paying the fee.
    private UgcOxEvent Entered(bool start) {
        var ox = new UgcOxEvent(HostAccount, Host, "Host", 62000001, new UgcOxRules.Settings(Fee, 10, 50));
        ox.Enter(test.CharacterId, _ => true);
        ox.Enter(Other, _ => true);
        if (start) {
            Assert.That(ox.Start(), Is.True);
        }
        return ox;
    }

    private List<(long Receiver, long Meso, string Content)> Mails() {
        using var context = new Ms2Context(test.Options);
        return context.Mail.AsNoTracking().AsEnumerable()
            .Select(mail => (mail.ReceiverId, mail.Currency.Meso, mail.Content))
            .OrderBy(mail => mail.ReceiverId)
            .ToList();
    }

    private static string Notice(StringCode code, long amount) =>
        Convert.ToHexString(NoticePacket.Message(new InterfaceText(code, $"{amount}")).ToArray());

    // The game ran: the one winner, in the stadium, gets half the pot there; the host, not there, gets the rest
    // by mail.
    [Test]
    public void Prize_ToAWinnerInTheStadium_IsPaidThere_TheHostsShareIsMailed() {
        UgcOxEvent ox = Entered(start: true);
        long meso = test.Session.Currency.Meso;

        factory.CloseUgcOx(ox, [test.CharacterId], refundAll: false, test.Session.Field);

        Assert.Multiple(() => {
            Assert.That(test.Session.Currency.Meso - meso, Is.EqualTo(Fee));
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(Notice(StringCode.s_ugcox_entry_prize_meso_get, Fee)));
            Assert.That(Mails(), Is.EqualTo(new[] { (Host, Fee, "Your mesos for hosting the OX Quiz.") }));
        });
    }

    // A canceled event refunds every entrant by mail, even one in the stadium.
    [Test]
    public void Refund_OfACanceledEvent_IsMailed() {
        UgcOxEvent ox = Entered(start: false);
        long meso = test.Session.Currency.Meso;

        factory.CloseUgcOx(ox, [], refundAll: true, test.Session.Field);

        const string text = "The OX Quiz you entered did not take place. Your entry fee is returned.";
        Assert.Multiple(() => {
            Assert.That(test.Session.Currency.Meso, Is.EqualTo(meso));
            Assert.That(Mails(), Is.EqualTo(new[] { (test.CharacterId, Fee, text), (Other, Fee, text) }));
        });
    }

    // An event that never started returns the fees though no refund-all was asked: the entrant in the stadium gets
    // it there, the other by mail.
    [Test]
    public void Refund_OfAnEventThatNeverRan_PaysTheEntrantHere() {
        UgcOxEvent ox = Entered(start: false);
        long meso = test.Session.Currency.Meso;

        factory.CloseUgcOx(ox, [], refundAll: false, test.Session.Field);

        Assert.Multiple(() => {
            Assert.That(test.Session.Currency.Meso - meso, Is.EqualTo(Fee));
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(Notice(StringCode.s_ugcox_entry_fee_refund_meso_get, Fee)));
            Assert.That(Mails(), Is.EqualTo(new[] { (Other, Fee, "The OX Quiz you entered did not take place. Your entry fee is returned.") }));
        });
    }

    // A second close pays nothing again.
    [Test]
    public void SecondClose_PaysNothing() {
        UgcOxEvent ox = Entered(start: false);
        factory.CloseUgcOx(ox, [], refundAll: true, null);

        factory.CloseUgcOx(ox, [], refundAll: true, null);

        Assert.That(Mails(), Has.Count.EqualTo(2));
    }
}

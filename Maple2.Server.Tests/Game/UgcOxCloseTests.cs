using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Tests.Game;

// The end of a player-hosted OX quiz (FieldManager.Factory.CloseUgcOx): its money is settled once and paid, at once
// with the client's notice to a payee in the stadium, by system mail to anyone else; the event leaves the channel.
public class UgcOxCloseTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long Host = 777000;
    private const long Absent = 888000;

    private TestGameSession test = null!;
    private FieldManager.Factory factory = null!;
    private ConcurrentDictionary<long, UgcOxEvent> events = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        factory = (FieldManager.Factory) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager.Factory));
        events = new ConcurrentDictionary<long, UgcOxEvent>();
        Set("ugcOxEvents", events);
        Set("fields", new ConcurrentDictionary<int, ConcurrentDictionary<int, FieldManager>>());
        Set("homes", new ConcurrentDictionary<long, HomeFieldManager>());
        Set("logger", Log.Logger.ForContext<FieldManager.Factory>());
        Set("<GameStorage>k__BackingField", test.Storage);
        test.Session.Currency.Meso = 0;
        test.TakeSent();
    }

    private void Set(string name, object value) => typeof(FieldManager.Factory).GetField(name, Any)!.SetValue(factory, value);

    // An event with the test player and an absent player entered (1000 each), started.
    private UgcOxEvent Running() {
        var ox = new UgcOxEvent(5, Host, "host", 62000001, new UgcOxRules.Settings(1000, 10, 50));
        Assert.That(ox.Enter(test.Session.CharacterId, _ => true), Is.EqualTo(UgcOxEnter.New));
        Assert.That(ox.Enter(Absent, _ => true), Is.EqualTo(UgcOxEnter.New));
        Assert.That(ox.Start(), Is.True);
        events[ox.HostAccountId] = ox;
        return ox;
    }

    private Maple2.Database.Model.Mail[] MailsTo(long characterId) {
        using var context = new Ms2Context(test.Options);
        return context.Mail.Where(mail => mail.ReceiverId == characterId).ToArray();
    }

    // A game that ran: the winner in the stadium is paid its part at once with the prize notice; the host, not there,
    // gets the rest by mail. The event is gone from the channel.
    [Test]
    public void AGameThatRan_PaysTheWinnerAtOnce_AndMailsTheHost() {
        UgcOxEvent ox = Running();
        UgcOxRules.Split split = UgcOxRules.Payout(2000, 50, 1);

        factory.CloseUgcOx(ox, [test.Session.CharacterId], refundAll: false, stadium: test.Session.Field);

        Assert.That(test.Session.Currency.Meso, Is.EqualTo(split.EachWinner));
        Assert.That(test.TakeSentOps(), Does.Contain(SendOp.Notice));
        Assert.That(MailsTo(Host).Select(mail => mail.Type), Is.EqualTo(new[] { MailType.System }));
        Assert.That(MailsTo(Absent), Is.Empty);
        Assert.That(events, Is.Empty);
        Assert.That(ox.Open, Is.False);
    }

    // The server's shutdown: every entrant's fee goes back by mail, even to one in the stadium, whose session may be
    // saved already.
    [Test]
    public void RefundAll_MailsEveryFee() {
        UgcOxEvent ox = Running();

        factory.CloseUgcOx(ox, [], refundAll: true, stadium: test.Session.Field);

        Assert.That(test.Session.Currency.Meso, Is.Zero);
        Assert.That(MailsTo(test.Session.CharacterId), Has.Length.EqualTo(1));
        Assert.That(MailsTo(Absent), Has.Length.EqualTo(1));
        Assert.That(MailsTo(Host), Is.Empty);
    }

    // A removed event (its stadium never reached the end) refunds the entrants; a second close pays nothing more.
    [Test]
    public void RemoveUgcOx_Refunds_AndASecondClosePaysNothing() {
        UgcOxEvent ox = Running();

        factory.RemoveUgcOx(ox);
        factory.CloseUgcOx(ox, [test.Session.CharacterId], refundAll: false, stadium: test.Session.Field);

        Assert.That(MailsTo(test.Session.CharacterId), Has.Length.EqualTo(1));
        Assert.That(MailsTo(Absent), Has.Length.EqualTo(1));
        Assert.That(test.Session.Currency.Meso, Is.Zero);
    }
}

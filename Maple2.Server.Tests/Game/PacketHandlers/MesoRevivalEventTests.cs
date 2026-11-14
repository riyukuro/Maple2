using System;
using System.Reflection;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The meso revival event (MesoRevival): while one for everyone runs, the meso revive in place (Revival 2) costs
// CalcRevivalMeso(level, remaining, 1) = 10000 and is not held to the day's count. The ids are made up. A revive that
// goes through needs the passive skill and job tables a test session lacks, so the event's side is tested on the parts
// the handler puts together; the handler is tested on the refusal it keeps without an event for everyone.
public class MesoRevivalEventTests {
    private const int Town = 2000001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Start(params (int Id, MesoRevival Data, bool Ended)[] events) {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        foreach ((int id, MesoRevival data, bool ended) in events) {
            test.AddEvent(id, GameEventType.MesoRevival, data, end: ended ? DateTime.Now.AddDays(-1) : null);
        }
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Player.Value.Character.Level = 30; // the day's price: 10000 + 20 * 1000
        session.Currency.Meso = 100000;
        test.TakeSent();
    }

    private void Die() {
        typeof(Maple2.Server.Game.Manager.ConfigManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Config, session);
        typeof(FieldPlayer).GetProperty("IsDead")!.GetSetMethod(true)!.Invoke(session.Player, [true]);
        session.Player.Tombstone = new Tombstone(session.Player, 0);
    }

    private void Revive() {
        new RevivalHandler { ServerTableMetadata = test.ServerTableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2); // InstantRevive
            writer.WriteBool(false); // mesos, not a voucher
        }));
    }

    // The price the handler takes: CalcRevivalMeso with the event flag, then the same fee cut (none here).
    [TestCase(30, 3, false, 30000L)]
    [TestCase(30, 3, true, 10000L)]
    [TestCase(30, 0, true, 10000L)] // the count left does not change the event's price
    [TestCase(1, 3, true, 10000L)]
    public void Price_WithTheEventFlag(int level, int remaining, bool mesoEvent, long expected) {
        long cost = RevivalRules.MesoCost(Maple2.Server.Game.LuaFunctions.Lua.CalcRevivalMeso(level, remaining, mesoEvent ? 1 : 0),
            0.5f, 0f, homeField: false);
        Assert.That(cost, Is.EqualTo(expected));
    }

    [TestCase(true, false)] // a PC-bang-only event: no player here is one
    [TestCase(false, true)] // ended
    public void NoEventForEveryone_CountUsedUp_IsRefused(bool pcBangOnly, bool ended) {
        Start((140001, new MesoRevival(pcBangOnly), ended));
        session.Config.InstantReviveCount = 3;
        Die();

        Revive();

        Assert.Multiple(() => {
            Assert.That(session.Player.IsDead, Is.True);
            Assert.That(session.Currency.Meso, Is.EqualTo(100000));
        });
    }

    [Test]
    public void Rules_EventForEveryoneOnly() {
        TestGameSession events = new();
        Assert.Multiple(() => {
            Assert.That(RevivalRules.MesoEventOn([events.AddEvent(1, GameEventType.MesoRevival, new MesoRevival(false))]), Is.True);
            Assert.That(RevivalRules.MesoEventOn([events.AddEvent(2, GameEventType.MesoRevival, new MesoRevival(true))]), Is.False);
            Assert.That(RevivalRules.MesoEventOn([]), Is.False);
            Assert.That(RevivalRules.MesoRevive(true, false, false, false, false, 3, 3, 10000, 10000, mesoEvent: true),
                Is.EqualTo(MesoReviveRefusal.None));
            Assert.That(RevivalRules.MesoRevive(true, false, false, false, false, 3, 3, 10000, 10000),
                Is.EqualTo(MesoReviveRefusal.DailyMax));
            Assert.That(RevivalRules.MesoRevive(true, false, false, false, false, 3, 3, 9999, 10000, mesoEvent: true),
                Is.EqualTo(MesoReviveRefusal.LackMeso)); // the event lifts the count, not the price
            Assert.That(RevivalRules.CountsTowardDailyMax(homeField: false, mesoEvent: true), Is.False);
            Assert.That(RevivalRules.CountsTowardDailyMax(homeField: false, mesoEvent: false), Is.True);
        });
    }

    [TestCase(null, false)]
    [TestCase("", false)] // all KR rows
    [TestCase(" ", false)]
    [TestCase("0", false)]
    [TestCase("false", false)]
    [TestCase("1", true)]
    [TestCase("TRUE", true)]
    public void Reader_ParseBoolWithoutADefault(string? value1, bool pcBangOnly) {
        Assert.That(EventSaleReader.MesoRevival(value1), Is.EqualTo(new MesoRevival(pcBangOnly)));
    }

    [Test]
    public void Event_WritesTheIdAndTheFlag() {
        GameEvent gameEvent = new TestGameSession().AddEvent(140001, GameEventType.MesoRevival, new MesoRevival(true));
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var name = new ByteWriter();
        name.WriteUnicodeString("MesoRevival");
        Assert.That(Convert.ToHexString(writer.ToArray()), Is.EqualTo(Convert.ToHexString(name.ToArray()) + "E1220200" + "01"));
    }
}

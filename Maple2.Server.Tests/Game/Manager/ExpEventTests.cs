using System;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The exp events of the event list: AdventureLevelBonusExpRate (prestige exp, KR 3 rows of 10000), GuildExp (KR 1
// row of 10000, World side in GuildFundTests), MasteryReduce (KR 1 row of 30000, a shorter gather time the client
// keeps) and PetMastery (KR 2 rows "1" / "10000"): the shared share rule, the prestige gain, the row readers and the
// event list entries. The ids are made up.
public class ExpEventTests {
    private TestGameSession test = null!;

    private GameSession Start(params (int Id, int Rate, bool Over)[] events) {
        test = new TestGameSession();
        foreach ((int id, int rate, bool over) in events) {
            test.AddEvent(id, GameEventType.AdventureLevelBonusExpRate, new EventRate(rate),
                over ? DateTime.Now.AddDays(-9) : null, over ? DateTime.Now.AddDays(-2) : null);
        }
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { AdventureLevelStartLevel = 50 });
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000173));
        session.Player.Value.Character.Level = 60;
        test.TakeSent();
        return session;
    }

    private long PrestigeSent() {
        byte[]? packet = test.TakeSent().Find(packet => TestGameSession.Op(packet) == SendOp.Prestige && packet[2] == 1);
        return packet == null ? 0 : BitConverter.ToInt64(packet, 11);
    }

    // gain * rate / 10000, cut to 32 bits unsigned (CGameRule::CalcAdventureLevelBonusExp); nothing without a gain
    // or a rate.
    [TestCase(1000L, 10000, 1000L)]
    [TestCase(1000L, 2500, 250L)]
    [TestCase(3L, 5000, 1L)]
    [TestCase(1L, 9999, 0L)]
    [TestCase(1000L, 0, 0L)]
    [TestCase(0L, 10000, 0L)]
    [TestCase(-100L, 10000, 0L)]
    [TestCase(5000000000L, 10000, 705032704L)]
    public void EventBonus_IsTheSharedRule(long gain, int rate, long bonus) {
        Assert.That(EventSaleRules.EventBonus(gain, rate), Is.EqualTo(bonus));
    }

    // A running event adds its share to the prestige gain, sent and kept; the highest rate counts; an ended event
    // adds nothing.
    [TestCase(0, false, 1000L)]
    [TestCase(10000, false, 2000L)]
    [TestCase(10000, true, 1000L)]
    public void Prestige_EventAddsItsShare(int rate, bool over, long gained) {
        GameSession session = rate > 0 ? Start((730001, rate, over)) : Start();
        long before = session.Exp.PrestigeCurrentExp;

        session.Exp.AddPrestigeExp(1000);

        Assert.That(PrestigeSent(), Is.EqualTo(gained));
        Assert.That(session.Exp.PrestigeCurrentExp - before, Is.EqualTo(gained));
    }

    [Test]
    public void Prestige_HighestRateCounts() {
        GameSession session = Start((730001, 2500, false), (730003, 5000, false));
        long before = session.Exp.PrestigeCurrentExp;

        session.Exp.AddPrestigeExp(1000);

        Assert.That(session.Exp.PrestigeCurrentExp - before, Is.EqualTo(1500));
    }

    // value1 the kinds, value2 the rates: the last place of a kind counts, other kinds are left out; lists that
    // differ, a place that is not a number or no positive rate give no event.
    [Test]
    public void PetMastery_ReadsTheRow() {
        Assert.Multiple(() => {
            Assert.That(EventSaleReader.PetMastery("1", "10000"), Is.EqualTo(new PetMastery(10000, 0)));
            Assert.That(EventSaleReader.PetMastery(" 1, 2 ", "5000,3000"), Is.EqualTo(new PetMastery(5000, 3000)));
            Assert.That(EventSaleReader.PetMastery("2,2,3", "100,200,300"), Is.EqualTo(new PetMastery(0, 200)));
            Assert.That(EventSaleReader.PetMastery("1,2", "5000"), Is.Null);
            Assert.That(EventSaleReader.PetMastery("", ""), Is.Null);
            Assert.That(EventSaleReader.PetMastery("1", "x"), Is.Null);
            Assert.That(EventSaleReader.PetMastery("3", "10000"), Is.Null);
            Assert.That(EventSaleReader.PetMastery("1", "0"), Is.Null);
            Assert.That(EventSaleReader.PetMastery("1,2", "5000,-100"), Is.Null); // a negative rate drops the row
            Assert.That(EventSaleReader.PetMastery("1,1", "-100,5000"), Is.EqualTo(new PetMastery(5000, 0))); // overridden
        });
    }

    // GameEventPetMastery::vfunc_4 reads the id, the capture rate and the other rate; the rate events read the id and
    // the rate.
    [Test]
    public void Events_WriteWhatTheClientReads() {
        var session = new TestGameSession();
        Assert.Multiple(() => {
            Assert.That(Written(session.AddEvent(590001, GameEventType.PetMastery, new PetMastery(10000, 3000)), "PetMastery"),
                Is.EqualTo("B1000900" + "10270000" + "B80B0000"));
            Assert.That(Written(session.AddEvent(20180, GameEventType.GuildExp, new EventRate(10000)), "GuildExp"),
                Is.EqualTo("D44E0000" + "10270000"));
            Assert.That(Written(session.AddEvent(520001, GameEventType.MasteryReduce, new EventRate(30000)), "MasteryReduce"),
                Is.EqualTo("41EF0700" + "30750000"));
            Assert.That(Written(session.AddEvent(730001, GameEventType.AdventureLevelBonusExpRate, new EventRate(10000)), "AdventureLevelBonusExpRate"),
                Is.EqualTo("91230B00" + "10270000"));
        });
    }

    private static string Written(GameEvent gameEvent, string name) {
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var prefix = new ByteWriter();
        prefix.WriteUnicodeString(name);
        string hex = Convert.ToHexString(writer.ToArray());
        string head = Convert.ToHexString(prefix.ToArray());
        return hex.StartsWith(head) ? hex[head.Length..] : "name " + hex;
    }
}

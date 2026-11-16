using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The home rest exp tick, the monster exp fallback to the exp table and the field boss bonus. Made-up table rows.
public class ExperienceFallbackTests {
    private TestGameSession test = null!;

    private GameSession Start(int restRate, bool withNext = true, params (int Level, long Exp)[] monsterRows) {
        test = new TestGameSession();
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, _ => 100_000L),
            [2] = new Dictionary<int, long> { [60] = 777 },
        }, withNext ? Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L) : new Dictionary<int, long>()));
        test.SetServerTable(new MonsterExpTable(monsterRows.ToDictionary(row => row.Level, row => new MonsterExpMetadata(row.Exp, 0, 0, 0, 0))));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { RestExpMaxAcquireRate = restRate });
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000173));
        session.Player.Value.Character.Level = 50;
        test.TakeSent();
        return session;
    }

    // One tick adds the step (35001/10^7 of the level's exp, at least 1) of rest exp and sends it as ExpUp.
    [Test]
    public void TickRestExp_AddsAStepAndSendsIt() {
        GameSession session = Start(1000);

        session.Exp.TickRestExp();

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Character.RestExp, Is.EqualTo(350));
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.ExpUp }));
        });
    }

    // At the cap (a zero rate here) and with no exp for the level, nothing is added or sent.
    [TestCase(0, true)]
    [TestCase(1000, false)]
    public void TickRestExp_WithoutRoomOrRowSendsNothing(int rate, bool withNext) {
        GameSession session = Start(rate, withNext);

        session.Exp.TickRestExp();

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Character.RestExp, Is.EqualTo(0));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    // The monster exp table is read first; without its row the exp table's second column gives the exp, and
    // without either the kill is worth 0.
    [Test]
    public void MobExp_FallsBackToTheExpTable() {
        GameSession session = Start(0, true, (50, 123));

        Assert.Multiple(() => {
            Assert.That(session.Exp.MobExp(50, 1), Is.EqualTo(123));
            Assert.That(session.Exp.MobExp(60, 1), Is.EqualTo(777));
            Assert.That(session.Exp.MobExp(70, 1), Is.EqualTo(0));
        });
    }

    // The field boss bonus is 30% of the kill exp and is added to the character.
    [Test]
    public void FieldBossBonus_IsAThirdOfTheKillExpAdded() {
        GameSession session = Start(0);
        long before = session.Player.Value.Character.Exp;

        long gain = session.Exp.AddFieldBossBonusExp(1000, 7001);

        Assert.Multiple(() => {
            Assert.That(gain, Is.EqualTo(300));
            Assert.That(session.Player.Value.Character.Exp, Is.EqualTo(before + 300));
        });
    }
}

using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// A Maple Survival game's result gives the character exp of the commonexptable row mapleSurvival (KR: exp base table 1,
// factor 1) and the prestige exp of the server pack's Exp_mapleSurvival (KR 250), once a game. Before, nothing gave
// either: the rows were ingested and never read.
public class SurvivalResultExpTests {
    private const long Base = 1234; // the test's exp base table 1 at every level
    private const long Row = 250;   // KR Exp_mapleSurvival
    private TestGameSession test = null!;

    private GameSession Start(short level, long nextExp = 50_000_000L) {
        test = new TestGameSession();
        test.SetTable(new MapleSurvivalTable(
            new Dictionary<int, MapleSurvivalTable.Period>(),
            new Dictionary<int, long> { [1] = 0, [2] = 1_000_000 },
            new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>()));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> {
            [ExpType.mapleSurvival] = new(1, 1f),
        }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(lv => lv, _ => Base),
        }, Enumerable.Range(1, 99).ToDictionary(lv => lv, _ => nextExp)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long> { [ExpType.mapleSurvival] = Row }));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        test.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata>()));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { AdventureLevelStartLevel = 50, characterMaxLevel = 99 });
        GameSession session = test.Start();
        // What a level up reads: the config's skill tree, stat points and hot bars.
        ProgressionOrderTests.LoadConfig(test, session);
        test.EnterMap(TestGameSession.Map(2000062));
        session.Player.Value.Character.Level = level;
        session.Survival = new SurvivalManager(session);
        test.TakeSent();
        return session;
    }

    [Test]
    public void Result_GivesTheGamesExpAndPrestige() {
        GameSession session = Start(60);
        long exp = session.Player.Value.Character.Exp;
        long prestige = session.Exp.PrestigeCurrentExp;

        int[] values = MapleSurvivalProgress.Result(session, 3, 10, 1, 120, out _);

        Assert.That(session.Player.Value.Character.Exp - exp, Is.EqualTo(Base));
        Assert.That(session.Exp.PrestigeCurrentExp - prestige, Is.EqualTo(Row));
        // The window's character and prestige levels are read after the gain.
        Assert.That((values[12], values[13]), Is.EqualTo((60, session.Player.Value.Account.PrestigeLevel)));
    }

    // A game whose exp reaches the next level: the window shows the level the game's exp reached.
    [Test]
    public void Result_LevelUp_WindowShowsTheReachedLevel() {
        GameSession session = Start(60, nextExp: Base);

        int[] values = MapleSurvivalProgress.Result(session, 3, 10, 1, 120, out _);

        Assert.That(session.Player.Value.Character.Level, Is.EqualTo(61));
        Assert.That(values[12], Is.EqualTo(61));
    }

    // Below the prestige start level the exp is given and no prestige, as for every gain.
    [Test]
    public void Result_BelowTheStartLevel_ExpOnly() {
        GameSession session = Start(40);
        long exp = session.Player.Value.Character.Exp;
        long prestige = session.Exp.PrestigeCurrentExp;

        MapleSurvivalProgress.Result(session, 3, 10, 1, 120, out _);

        Assert.That((session.Player.Value.Character.Exp - exp, session.Exp.PrestigeCurrentExp - prestige), Is.EqualTo((Base, 0L)));
    }

    // A held improve_survival_kill_exprate buff (KR 70000176: 10) raises the survival exp of the game's kills by 10 %:
    // rank 3 of 10 with 1 kill gives 1000 + 330 + 1400, not 1000 + 300 + 1400.
    [TestCase(0f, 2700L)]
    [TestCase(10f, 2730L)]
    public void Result_KillExpRateRaisesTheKillPart(float rate, long gained) {
        GameSession session = Start(60);
        session.Player.Stats.Values[SpecialAttribute.ImproveSurvivalKillExp].AddTotal(new SpecialOption(rate));
        long before = session.Player.Value.Account.SurvivalExp;

        MapleSurvivalProgress.Result(session, 3, 10, 1, 120, out _);

        Assert.That(session.Player.Value.Account.SurvivalExp - before, Is.EqualTo(gained));
    }
}

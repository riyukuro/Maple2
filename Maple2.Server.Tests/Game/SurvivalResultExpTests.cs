using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A Maple Survival game's result gives the character exp of the commonexptable row mapleSurvival (KR: exp base table 1,
// factor 1) and the prestige exp of the server pack's Exp_mapleSurvival (KR 250), once a game. Before, nothing gave
// either: the rows were ingested and never read.
public class SurvivalResultExpTests {
    private const long Base = 1234; // the test's exp base table 1 at every level
    private const long Row = 250;   // KR Exp_mapleSurvival
    private TestGameSession test = null!;

    private GameSession Start(short level) {
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
        }, Enumerable.Range(1, 99).ToDictionary(lv => lv, _ => 50_000_000L)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long> { [ExpType.mapleSurvival] = Row }));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { AdventureLevelStartLevel = 50 });
        GameSession session = test.Start();
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

    // Below the prestige start level the exp is given and no prestige, as for every gain.
    [Test]
    public void Result_BelowTheStartLevel_ExpOnly() {
        GameSession session = Start(40);
        long exp = session.Player.Value.Character.Exp;
        long prestige = session.Exp.PrestigeCurrentExp;

        MapleSurvivalProgress.Result(session, 3, 10, 1, 120, out _);

        Assert.That((session.Player.Value.Character.Exp - exp, session.Exp.PrestigeCurrentExp - prestige), Is.EqualTo((Base, 0L)));
    }
}

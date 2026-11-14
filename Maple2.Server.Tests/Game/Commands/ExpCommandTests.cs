using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.Commands;

// GM "player exp N" adds N exp. Before, N went in as the multiplier of the expDrop row (AddExp(ExpType, float)): at
// level 50 on KR, `exp 1000` gave 248726 x 1000 x 0.4 = 99490400 exp, two levels (50 -> 52).
public class ExpCommandTests {
    private TestGameSession test = null!;

    // The KR rows: expDrop (factor 0.4, exp base table 1), base table 1 at level 50, and the next exp at 50 and 51.
    private GameSession Start() {
        test = new TestGameSession();
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> {
            [ExpType.expDrop] = new(1, 0.4f),
        }));
        Dictionary<int, long> nextExp = Enumerable.Range(1, 99).ToDictionary(level => level, _ => 50_000_000L);
        nextExp[50] = 46784667;
        nextExp[51] = 47252514;
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, level => level == 50 ? 248726L : 1000L),
        }, nextExp));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        test.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { characterMaxLevel = 99 });
        GameSession session = test.Start();
        // What a level up reads: the config's skill tree, stat points and hot bars.
        ProgressionOrderTests.LoadConfig(test, session);
        test.EnterMap(TestGameSession.Map(2000062));
        session.Player.Value.Character.Level = 50;
        test.TakeSent();
        return session;
    }

    [Test]
    public void Exp_AddsTheAmount() {
        GameSession session = Start();
        var console = new TestConsole();

        Assert.That(new PlayerCommand(session, null!).Invoke(["exp", "1000"], console), Is.EqualTo(0), console.Error.ToString());

        Assert.That(session.Player.Value.Character.Level, Is.EqualTo(50));
        Assert.That(session.Player.Value.Character.Exp, Is.EqualTo(1000));
    }

    [Test]
    public void Exp_OfTheNextExp_IsOneLevel() {
        GameSession session = Start();

        Assert.That(new PlayerCommand(session, null!).Invoke(["exp", "46784667"], new TestConsole()), Is.EqualTo(0));

        Assert.That(session.Player.Value.Character.Level, Is.EqualTo(51));
        Assert.That(session.Player.Value.Character.Exp, Is.EqualTo(0));
    }
}

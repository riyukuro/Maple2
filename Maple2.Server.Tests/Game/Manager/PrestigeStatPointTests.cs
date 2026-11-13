using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The prestige stat points are held while the character is below AdventureLevelStartLevel: the adventure window shows
// the statPoint rows locked below it (uiadventureleveldialog.cpp:483) and the character window gives their reason
// text only from it (uipcinfodialog.cpp:778). They are counted again at a prestige and a character level up.
public class PrestigeStatPointTests {
    private TestGameSession test = null!;

    private GameSession Start(short level) {
        test = new TestGameSession();
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata> {
            [2] = new(2, PrestigeAwardType.statPoint, 0, 0, 1),
            [3] = new(3, PrestigeAwardType.item, 20000001, 1, 1),
            [4] = new(4, PrestigeAwardType.statPoint, 0, 0, 2),
        }));
        test.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata>()));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(l => l, l => l * 10L),
        }, Enumerable.Range(1, 99).ToDictionary(l => l, _ => 100L)));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { AdventureLevelStartLevel = 50, AdventureLevelLimit = 100, characterMaxLevel = 99 });
        GameSession session = test.Start();
        PacketHandlers.ProgressionOrderTests.LoadConfig(test, session);
        session.Player.Value.Character.Level = level;
        test.TakeSent();
        return session;
    }

    private int Points => test.Session.Config.StatPoints(AttributePointSource.Prestige);

    [Test]
    public void BelowTheLevel_NoPoints_FullAtTheLevel() {
        GameSession session = Start(49);

        session.Exp.PrestigeLevelUp(4);
        Assert.That(Points, Is.EqualTo(0), "an alt below the level holds them");

        session.Player.Value.Character.Exp = 100;
        session.Exp.LevelUp();
        Assert.That(session.Player.Value.Character.Level, Is.EqualTo(50));
        Assert.That(Points, Is.EqualTo(3), "at the level: every statPoint row up to prestige 4");
    }

    // A gain of several prestige levels counts the rows of the levels it passed over (levels 2 and 4).
    [Test]
    public void AtTheLevel_ASkipCountsEveryRow() {
        GameSession session = Start(50);

        session.Exp.PrestigeLevelUp(4);

        Assert.That(Points, Is.EqualTo(3));
    }
}

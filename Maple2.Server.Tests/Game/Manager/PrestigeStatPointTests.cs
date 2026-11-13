using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Config;
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

    // The GM level command sets the level directly; it counts the points again as a level up does, both ways.
    [Test]
    public void LevelCommand_CountsAgain() {
        GameSession session = Start(50);
        session.Exp.PrestigeLevelUp(4);
        Assert.That(Points, Is.EqualTo(3));

        Assert.That(new PlayerCommand(session, null!).Invoke(["level", "30"], new TestConsole()), Is.EqualTo(0));
        Assert.That(Points, Is.EqualTo(0), "set below the level: held again");

        Assert.That(new PlayerCommand(session, null!).Invoke(["level", "60"], new TestConsole()), Is.EqualTo(0));
        Assert.That(Points, Is.EqualTo(3), "set to the level or above: counted");
    }

    // At login the prestige source is counted again even when it was never stored: below the level it is 0, and a
    // stored allocation above the new total is reset; at the level the same allocation is kept.
    [TestCase((short) 49, true, 0, 0)]
    [TestCase((short) 50, true, 3, 3)]
    [TestCase((short) 50, false, 3, 3)]
    public void Login_CountsAgain_ResetsAnAllocationAboveTheTotal(short level, bool stored, int points, int used) {
        GameSession session = Start(level);
        session.Player.Value.Account.PrestigeLevel = 4;
        var allocation = new Dictionary<BasicAttribute, int> { [BasicAttribute.Strength] = 3 };

        session.Config.LoadStoredStatPoints(stored
            ? new Dictionary<AttributePointSource, int> { [AttributePointSource.Prestige] = 3 }
            : new Dictionary<AttributePointSource, int>(), allocation);

        Assert.That(Points, Is.EqualTo(points));
        var stats = (StatAttributes) typeof(ConfigManager).GetField("statAttributes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session.Config)!;
        Assert.That(stats.UsedPoints, Is.EqualTo(used));
    }
}

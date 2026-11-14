using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// adventure_level_up (ExperienceManager.PrestigeLevelUp), with the KR quest row's condition as the ingest reads it.
public class AdventureLevelConditionTests {
    // KR 89000512: <condition type="adventure_level_up" value="3" />, each prestige level gained counts, also each
    // level of a gain of several.
    [Test]
    public void AdventureLevelUp_89000512_EachLevelGained() {
        var test = new TestGameSession();
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        test.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata>()));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(l => l, l => l * 10L),
        }, Enumerable.Range(1, 99).ToDictionary(l => l, _ => 100L)));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { AdventureLevelStartLevel = 50, AdventureLevelLimit = 100, characterMaxLevel = 99 });
        GameSession session = test.Start();
        PacketHandlers.ProgressionOrderTests.LoadConfig(test, session);
        session.Player.Value.Character.Level = 50;
        Quest quest = test.StartQuest(TestGameSession.QuestData(89000512) with {
            Conditions = [new ConditionMetadata(ConditionType.adventure_level_up, 3, new string[0].ConvertCodes(), new string[0].ConvertCodes())],
        });

        session.Exp.PrestigeLevelUp(2);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(2));
        session.Exp.PrestigeLevelUp();

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(3));
    }
}

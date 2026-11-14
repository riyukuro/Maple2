using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
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

    // KR 21100052 "Reach Prestige Rank {0}" (adventure_level, grades 10, 20, ...): the counter is the rank reached,
    // not the levels of the last gain.
    [Test]
    public void AdventureLevel_21100052_RankReached() {
        const int trophy = 21100052;
        var test = new TestGameSession();
        test.AddAchievement(new AchievementMetadata(trophy, $"{trophy}", true, AchievementCategory.Life, [],
            new Dictionary<int, AchievementMetadataGrade> {
                [1] = new(1, new ConditionMetadata(ConditionType.adventure_level, 10, null, null), null),
                [2] = new(2, new ConditionMetadata(ConditionType.adventure_level, 20, null, null), null),
            }, false));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        test.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata>()));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { AdventureLevelStartLevel = 50, AdventureLevelLimit = 100, characterMaxLevel = 99 });
        GameSession session = test.Start();
        PacketHandlers.ProgressionOrderTests.LoadConfig(test, session);
        session.Player.Value.Character.Level = 50;
        session.Exp.PrestigeLevel = 9;

        session.Exp.PrestigeLevelUp();
        Assert.That(test.TrophyCounter(trophy), Is.EqualTo(10));
        session.Exp.PrestigeLevelUp(2);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(test.TrophyCounter(trophy), Is.EqualTo(12));
    }
    // Prestige 2 (level up): the client finds the PC by its object id and sets the level and plays the effect for
    // any PC in the field, so another player in the field gets it too, with the levelling player's object id.
    [Test]
    public void PrestigeLevelUp_GoesToTheField() {
        var a = new TestGameSession();
        a.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        a.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata>()));
        a.SetServerTable(a.ServerTableMetadata.ConstantsTable with { AdventureLevelStartLevel = 50, AdventureLevelLimit = 100, characterMaxLevel = 99 });
        GameSession session = a.Start();
        PacketHandlers.ProgressionOrderTests.LoadConfig(a, session);
        session.Player.Value.Character.Level = 50;
        session.Exp.PrestigeLevel = 9;
        var b = new TestGameSession(a.Options);
        GameSession other = b.Start();
        FieldManager field = session.Field!;
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(other.Player, 2);
        field.Players[2] = other.Player;
        other.Field = field;
        a.TakeSent();
        b.TakeSent();

        session.Exp.PrestigeLevelUp();

        Assert.That(a.Errors, Is.Empty);
        byte[] expected = Packet(session.Player.ObjectId, 10);
        Assert.That(a.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.Prestige && p[2] == 2).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(expected) }));
        Assert.That(b.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.Prestige).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(expected) }));
    }

    private static byte[] Packet(int objectId, int level) {
        var writer = Maple2.Server.Game.Packets.PrestigePacket.LevelUp(objectId, level);
        return writer.Buffer[..writer.Length];
    }
}

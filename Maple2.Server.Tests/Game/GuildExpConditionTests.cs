using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Tests.Game;

// 74000029: <condition type="guild_exp" value="100" />: the guild exp the player's own contribution added counts, in
// exp; a guild exp update another member's contribution made does not, nor one that does not raise the exp.
public class GuildExpConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void GuildExp_74000029_TheOwnContribution() {
        var test = new TestGameSession();
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property with { Level = 1, Experience = 100_000, Capacity = 50 } }));
        GameSession session = test.Start();
        var guild = new Guild(1001, "Guild", test.AccountId, test.CharacterId, "Master");
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        GC.SuppressFinalize(manager);
        typeof(GuildManager).GetField("session", Any)!.SetValue(manager, session);
        typeof(GuildManager).GetField("logger", Any)!.SetValue(manager, new LoggerConfiguration().CreateLogger());
        typeof(GuildManager).GetField("<Guild>k__BackingField", Any)!.SetValue(manager, guild);
        typeof(GuildManager).GetField("<Properties>k__BackingField", Any)!.SetValue(manager, property with { Capacity = 50 });
        session.Guild = manager;
        Quest quest = test.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [new ConditionMetadata(ConditionType.guild_exp, 100, null, null)],
        });

        Assert.That(manager.UpdateGuildExpFunds(test.CharacterId + 1, 40, 0), Is.True);
        Assert.That(manager.UpdateGuildExpFunds(test.CharacterId, 40, 0), Is.True);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Assert.That(manager.UpdateGuildExpFunds(test.CharacterId, 70, 0), Is.True);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(30));
    }
}

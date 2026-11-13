using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Manager;

// require progressQuest, unrequire and guildNpcLevel (CQuestManagerCommon::CanAcceptQuest, questdata.cpp:501-517
// and 569) and dayOfWeek (vfunc_o10_21, questdata.cpp:342): CanStart refuses a quest the client would not accept.
// They gate accepting only: a held quest stays held when its require stops holding.
public class QuestRequireTests {
    private const int Other = 10001003;
    private const int Main = 10001004;
    private const int GuildQuest = 73000001;

    private TestGameSession test = null!;
    private GameSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        session = test.Session;
        test.Register(TestGameSession.QuestData(Other));
    }

    private QuestMetadata Register(QuestMetadataRequire require, int id = Main) {
        QuestMetadata quest = TestGameSession.QuestData(id);
        quest = quest with { Require = require };
        test.Register(quest);
        return quest;
    }

    private static QuestMetadataRequire Require() => TestGameSession.QuestData(Main).Require;

    private void Hold(QuestState state) {
        session.QuestMetadata.TryGet(Other, out QuestMetadata? metadata);
        test.StartQuest(metadata!).State = state;
    }

    // The player's guild with its quest npc at a level (0: no quest npc), or no guild (null).
    private void SetGuild(int? questNpcLevel) {
        Guild? guild = null;
        if (questNpcLevel != null) {
            guild = new Guild(39990001, "a", 1, 4000000001L, "a");
            if (questNpcLevel > 0) {
                guild.Npcs.Add(new GuildNpc(GuildNpcType.Quest, questNpcLevel.Value));
            }
        }
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        GC.SuppressFinalize(manager);
        typeof(GuildManager).GetField("<Guild>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(manager, guild);
        session.Guild = manager;
    }

    [TestCase(null, true)]
    [TestCase(QuestState.Started, false)]
    [TestCase(QuestState.Completed, false)]
    public void Unrequire(QuestState? held, bool canStart) {
        if (held != null) {
            Hold(held.Value);
        }
        Assert.That(session.Quest.CanStart(Register(Require() with { Unrequire = [Other] })), Is.EqualTo(canStart));
    }

    [TestCase(null, false)]
    [TestCase(QuestState.Started, true)]
    [TestCase(QuestState.Completed, false)]
    public void ProgressQuest(QuestState? held, bool canStart) {
        if (held != null) {
            Hold(held.Value);
        }
        Assert.That(session.Quest.CanStart(Register(Require() with { ProgressQuest = [Other] })), Is.EqualTo(canStart));
    }

    [TestCase(0, false, 0, true)]
    [TestCase(1, false, 0, false)] // no guild
    [TestCase(1, true, 0, false)]  // a guild without its quest npc
    [TestCase(1, true, 1, true)]
    [TestCase(1, true, 2, false)]  // exact: a level-2 guild takes only the level-2 groups
    [TestCase(2, true, 2, true)]
    public void MeetsGuildNpcLevel(int required, bool inGuild, int level, bool meets) {
        Assert.That(QuestRules.MeetsGuildNpcLevel(required, inGuild, level), Is.EqualTo(meets));
    }

    [TestCase(null, false)]
    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    public void CanStart_GuildNpcLevel(int? questNpcLevel, bool canStart) {
        SetGuild(questNpcLevel);
        Assert.That(session.Quest.CanStart(Register(Require() with { GuildNpcLevel = 1 }, GuildQuest)), Is.EqualTo(canStart));
    }

    // 2026-11-12 is a Thursday (5); 2026-11-15 a Sunday (1), 2026-11-14 a Saturday (7).
    [TestCase(2026, 11, 12, null, true)]
    [TestCase(2026, 11, 12, new[] { 4 }, false)]
    [TestCase(2026, 11, 11, new[] { 4 }, true)]
    [TestCase(2026, 11, 15, new[] { 1, 6, 7 }, true)]
    [TestCase(2026, 11, 14, new[] { 1, 6, 7 }, true)]
    [TestCase(2026, 11, 13, new[] { 1, 6, 7 }, true)]
    [TestCase(2026, 11, 12, new[] { 1, 6, 7 }, false)]
    public void OnWeekday(int year, int month, int day, int[]? weekdays, bool on) {
        Assert.That(QuestRules.OnWeekday(weekdays, new DateTime(year, month, day, 23, 59, 0)), Is.EqualTo(on));
    }

    [Test]
    public void CanStart_Weekday() {
        int today = (int) DateTime.Now.DayOfWeek + 1;
        int otherDay = today % 7 + 1;
        Assert.Multiple(() => {
            Assert.That(session.Quest.CanStart(Register(Require() with { Weekdays = [today] })), Is.True);
            Assert.That(session.Quest.CanStart(Register(Require() with { Weekdays = [otherDay] }, Main + 1)), Is.False);
        });
    }

    // Accepted at quest npc level 1; the guild then upgrades its npc to 2. The quest is no longer offered, but the
    // held one stays in progress and may still be completed (CanComplete reads only its conditions).
    [Test]
    public void HeldQuest_StaysWhenTheGuildNpcLevelChanges() {
        SetGuild(1);
        QuestMetadata metadata = Register(Require() with { GuildNpcLevel = 1 }, GuildQuest);
        Assume.That(session.Quest.CanStart(metadata), Is.True);
        test.StartQuest(metadata);

        SetGuild(2);
        Assert.Multiple(() => {
            Assert.That(session.Quest.CanStart(metadata), Is.False);
            Assert.That(session.Quest.TryGetQuest(GuildQuest, out Quest? quest) && quest.State == QuestState.Started, Is.True);
            Assert.That(session.Quest.TryGetQuest(GuildQuest, out Quest? held) && session.Quest.CanComplete(held), Is.True);
        });
    }
}

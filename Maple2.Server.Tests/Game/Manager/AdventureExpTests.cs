using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game.Manager;

// The adventure (prestige) exp of a kill, a quest and a mini game round: the server pack's Exp_KillMonster,
// Exp_EpicQuest / Exp_QuestETC and Exp_MiniGame rows, sent as Prestige 1 after the ExpUp, as KMS2 does.
public class AdventureExpTests {
    private TestGameSession test = null!;

    private GameSession Start(params AchievementMetadata[] trophies) {
        test = new TestGameSession();
        test.AddAchievement(trophies);
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> {
            [ExpType.miniGame] = new(1, 1f),
            [ExpType.userMiniGame] = new(1, 1f),
        }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, level => level * 10L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetServerTable(new MonsterExpTable(new Dictionary<int, MonsterExpMetadata> {
            [50] = new(100, 200, 300, 400, 500),
            [51] = new(225, 200, 300, 400, 500),
        }));
        // The KR server pack's rows (AdventureExpTable).
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long> {
            [ExpType.monster] = 250,
            [ExpType.quest] = 45000,
            [ExpType.mission] = 45000,
            [ExpType.questEtc] = 15000,
            [ExpType.epicQuest] = 15000,
            [ExpType.miniGame] = 75000,
            [ExpType.userMiniGame] = 15000,
            [ExpType.dailymission] = 45000,
            [ExpType.dailymissionLevelUp] = 45001, // KR 45000; apart here to tell the two rows apart
            [ExpType.dailyGuildQuest] = 75000,
            [ExpType.weeklyGuildQuest] = 150000,
            [ExpType.darkStream] = 150000,
        }));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000173));
        session.Player.Value.Character.Level = 50;
        test.TakeSent();
        return session;
    }

    // ExpUp: op, byte, i64 gain, u16 reason, ...
    private static int Reason(byte[] packet) => BitConverter.ToUInt16(packet, 11);

    // Prestige 1 (add exp): op, byte, i64 current, i64 gained.
    private static long PrestigeGain(byte[] packet) => BitConverter.ToInt64(packet, 11);

    // The reason of the gain's ExpUp and the prestige it added; the Prestige 1 follows the ExpUp.
    private (int Reason, long Prestige) Sent() {
        List<byte[]> sent = test.TakeSent();
        int expUp = sent.FindIndex(packet => TestGameSession.Op(packet) == SendOp.ExpUp);
        int prestige = sent.FindIndex(packet => TestGameSession.Op(packet) == SendOp.Prestige && packet[2] == 1);
        Assert.That(expUp, Is.Not.Negative);
        if (prestige < 0) {
            return (Reason(sent[expUp]), 0);
        }
        Assert.That(prestige, Is.GreaterThan(expUp));
        return (Reason(sent[expUp]), PrestigeGain(sent[prestige]));
    }

    // A kill's exp times the rate is taken in double: in float, 225 at a +4% stat rate came to 233.
    [Test]
    public void Kill_RateIsNotTruncatedByFloat() {
        GameSession session = Start();

        session.Exp.AddMobExp(51, 1, modifier: 1d + 40 / 1000.0, npcObjectId: 7001);

        // ExpUp: op, byte, i64 gain.
        byte[] expUp = test.TakeSent().First(packet => TestGameSession.Op(packet) == SendOp.ExpUp);
        Assert.That(BitConverter.ToInt64(expUp, 3), Is.EqualTo(234));
    }

    // KMS2 adds 250 for a common kill and 2500 for an elite (AdventureExpFactorElite 10); a boss is
    // AdventureExpFactorBoss (100).
    [TestCase(1, 250)]
    [TestCase(3, 2500)]
    [TestCase(4, 25000)]
    [TestCase(7, 250)]
    public void Kill_AddsTheKillRowByClass(int npcClass, long prestige) {
        GameSession session = Start();

        session.Exp.AddMobExp(50, npcClass, npcObjectId: 7001);

        Assert.That(Sent(), Is.EqualTo((60001, prestige)));
    }

    [Test]
    public void CustomExpKill_AddsTheKillRowToo() {
        GameSession session = Start();

        session.Exp.AddCustomKillExp(1234, 7001, npcClass: 3);

        Assert.That(Sent(), Is.EqualTo((60001, 2500L)));
    }

    // KMS2 adds 15000 for every quest, the epic, world, field and alliance quests and those named a mission's (1053).
    [TestCase(QuestType.EpicQuest, 20, 1010)]
    [TestCase(QuestType.EpicQuest, 5, 1053)]
    [TestCase(QuestType.WorldQuest, 50, 1010)]
    [TestCase(QuestType.AllianceQuest, 50, 1010)]
    [TestCase(QuestType.FieldMission, 50, 1053)]
    public void Quest_Adds15000(QuestType type, int standardLevel, int reason) {
        GameSession session = Start();

        session.Exp.AddQuestExp(5000, QuestExpRules.Reason(type, standardLevel).Message(), QuestExpRules.PrestigeType(type));

        Assert.That(Sent(), Is.EqualTo((reason, 15000L)));
    }

    // The navigator's daily missions and the guild's quests have their own rows in the server pack (KR: 709 daily
    // missions of guideType 4, 24 level and 10 level_boost guides; 40 daily and 20 weekly guild quests); they added
    // Exp_QuestETC's 15000. Completed through the quest manager, with a repeat and a guide type that give another row
    // when read the other way round.
    [TestCase(QuestType.DailyMission, 1, 4, 45000)]
    [TestCase(QuestType.DailyMission, 2, 3, 45000)]
    [TestCase(QuestType.DailyMission, 0, 1, 45001)]
    [TestCase(QuestType.DailyMission, 4, 2, 45001)]
    [TestCase(QuestType.GuildQuest, 2, 0, 75000)]
    [TestCase(QuestType.GuildQuest, 3, 0, 150000)]
    public void Quest_DailyMissionAndGuildQuest_AddTheirRows(QuestType type, int repeat, int guideType, long prestige) {
        GameSession session = Start();
        QuestMetadata data = TestGameSession.QuestData(90000041, type);
        Quest quest = test.StartQuest(data with {
            Basic = data.Basic with { Repeat = repeat, GuideType = guideType, StandardLevel = 20 },
            CompleteReward = data.CompleteReward with { Exp = 5000 },
        });
        test.TakeSent();

        Assert.That(session.Quest.Complete(quest), Is.True);

        Assert.That(Sent(), Is.EqualTo((1010, prestige)));
    }

    // A Dark Stream run's reward exp adds Exp_DarkStream (KR 150000); it added none. A run with no reward count left
    // is given no exp and adds none.
    [TestCase(1000, 150000)]
    [TestCase(0, 0)]
    public void DarkStreamReward_AddsTheDarkStreamRow(int exp, long prestige) {
        GameSession session = Start();
        long before = session.Exp.PrestigeCurrentExp;

        TriggerContext.GiveDarkStreamReward(session.Player, new DarkStreamGame.Reward(exp, 0, 0, []));

        Assert.That(session.Exp.PrestigeCurrentExp - before, Is.EqualTo(prestige));
    }

    // A round's prestige is the mini game row times the round's exp rate (KMS2: Crazy Runner 75000 at rate 1, Trap
    // Master 15000 at 0.2, Sole Survivor 18750 at 0.25), the massive user games included.
    [TestCase(ExpType.miniGame, 0.2f, 15000)]
    [TestCase(ExpType.miniGame, 0.25f, 18750)]
    [TestCase(ExpType.userMiniGame, 1f, 75000)]
    public void MiniGameRound_ScalesByTheExpRate(ExpType type, float rate, long prestige) {
        GameSession session = Start();

        Assert.That(session.Exp.AddExp(type, rate), Is.GreaterThan(0));

        Assert.That(Sent(), Is.EqualTo((3007, prestige)));
    }

    // KMS2: a day that started at 86000 (one level since), at 79250 a 15000 quest adds 6750 in full and 8250 at 0.02,
    // so 6915; the next adds 300.
    [Test]
    public void Gain_CrossingTheDaysFullAmountIsSplit() {
        GameSession session = Start();
        session.Exp.PrestigeLevel = 2;
        session.Exp.PrestigeLevelsGained = 1;
        session.Exp.PrestigeExp = 86000;
        session.Exp.PrestigeCurrentExp = 79250;

        session.Exp.AddQuestExp(5000, ExpMessageCode.quest, ExpType.epicQuest);
        Assert.That(Sent().Prestige, Is.EqualTo(6915));
        Assert.That(session.Exp.PrestigeCurrentExp, Is.EqualTo(86165));

        session.Exp.AddQuestExp(5000, ExpMessageCode.quest, ExpType.epicQuest);
        Assert.That(Sent().Prestige, Is.EqualTo(300));

        session.Exp.AddMobExp(50, 1, npcObjectId: 7001);
        Assert.That(Sent().Prestige, Is.EqualTo(5));
    }

    // Below the adventure level's start level a kill and a quest add no prestige.
    [Test]
    public void BelowTheStartLevel_NoPrestige() {
        GameSession session = Start();
        test.SetServerTable(new ConstantsTable(AdventureLevelStartLevel: 60));

        session.Exp.AddMobExp(50, 1, npcObjectId: 7001);
        session.Exp.AddQuestExp(5000, ExpMessageCode.quest, ExpType.questEtc);

        Assert.That(test.TakeSent().Exists(packet => TestGameSession.Op(packet) == SendOp.Prestige), Is.False);
    }

    // 22400002 "Every End Is a New Beginning": <condition type="exp" target="50">, the exp gained from level 50 on;
    // the exp of a level 49 character does not count. A kill's exp and a static gain both report the level.
    [Test]
    public void Exp_22400002_FromLevel50On() {
        const int trophy = 22400002;
        var target = new ConditionMetadata.Parameters(Integers: [50]);
        GameSession session = Start(new AchievementMetadata(trophy, $"{trophy}", false, AchievementCategory.Life, [],
            new Dictionary<int, AchievementMetadataGrade> {
                [1] = new(1, new ConditionMetadata(ConditionType.exp, 500_000_000, null, target), null),
            }, false));
        session.Player.Value.Character.Level = 49;
        session.Exp.AddStaticExp(100);
        session.Exp.AddMobExp(50, 1, npcObjectId: 7001);
        Assert.That(test.TrophyCounter(trophy) ?? 0, Is.EqualTo(0));

        session.Player.Value.Character.Level = 50;
        session.Exp.AddStaticExp(100);
        session.Exp.AddMobExp(50, 1, npcObjectId: 7001);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(test.TrophyCounter(trophy), Is.EqualTo(200));
    }
}

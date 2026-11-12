using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The trophy conditions that the managers and handlers report, driven through the real code on a TestGameSession:
// each counts once where it should and not where it should not. The trophy, quest, dungeon and map ids are made up.
public class TrophyWiringTests {
    private const int Trophy = 90000001;
    private const int OtherTrophy = 90000002;
    private const int Town = 90000100;

    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    private GameSession Start(params AchievementMetadata[] trophies) {
        test.AddAchievement(trophies);
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        test.TakeSent();
        return session;
    }

    private static long Goal => 1000;

    private List<SendOp> Sent() {
        List<SendOp> ops = test.TakeSentOps();
        Assert.That(test.Errors, Is.Empty);
        return ops;
    }

    #region killcount
    [Test]
    public void KillStreak_ReportsItsLengthAndKeepsTheLongest() {
        GameSession session = Start(TestGameSession.Trophy(Trophy, ConditionType.killcount, [Goal]));
        session.Player.Value.Character.Level = 10;

        session.Exp.OnKill(10, 1000);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve }));
        session.Exp.OnKill(10, 2000);
        session.Exp.OnKill(10, 3000);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(3));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve, SendOp.Achieve }));

        // A kill after the streak's time starts a new streak: nothing is sent until it passes the longest one.
        session.Exp.OnKill(10, 60000);
        session.Exp.OnKill(10, 61000);
        session.Exp.OnKill(10, 62000);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(3));
        Assert.That(Sent(), Is.Empty);
        session.Exp.OnKill(10, 63000);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(4));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve }));
    }
    #endregion

    #region use_merat
    [Test]
    public void Meret_SpentCountsAndGainedDoesNot() {
        GameSession session = Start(TestGameSession.Trophy(Trophy, ConditionType.use_merat, [Goal]));

        session.Currency.Meret += 500;
        Assert.That(test.TrophyCounter(Trophy), Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Meret }));

        session.Currency.Meret -= 120;
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(120));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Meret, SendOp.Achieve }));

        session.Currency.Meret -= 30;
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(150));
        // Setting the same amount spends nothing.
        session.Currency.Meret = session.Currency.Meret;
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(150));
    }
    #endregion

    #region hero_achieve_grade and locked
    [Test]
    public void TrophyGrade_CountsForTheTrophiesOfTheCodeRange() {
        // The hero trophy counts the grades of trophies 90000010-90000019.
        GameSession session = Start(
            TestGameSession.Trophy(Trophy, ConditionType.hero_achieve_grade, [Goal], codeRange: (90000010, 90000019)),
            TestGameSession.Trophy(90000010, ConditionType.use_merat, [10, 20, 1000]),
            TestGameSession.Trophy(90000020, ConditionType.home_goto, [1, 1000]));
        session.Currency.Meret = 10000;
        test.TakeSent();

        // One grade of a trophy in the range.
        session.Currency.Meret -= 10;
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        // No grade: nothing.
        session.Currency.Meret -= 1;
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        // The second grade.
        session.Currency.Meret -= 9;
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(2));
        // A grade of a trophy outside the range.
        session.ConditionUpdate(ConditionType.home_goto);
        Assert.That(test.TrophyCounter(90000020), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(2));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void TrophyGrade_TwoGradesAtOnceCountTwo() {
        GameSession session = Start(
            TestGameSession.Trophy(Trophy, ConditionType.hero_achieve_grade, [Goal], codeRange: (90000010, 90000019)),
            TestGameSession.Trophy(90000010, ConditionType.use_merat, [10, 20, 1000]));
        session.Currency.Meret = 10000;

        session.Currency.Meret -= 25;
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(2));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void LockedTrophy_IsNotCounted() {
        GameSession session = Start(
            TestGameSession.Trophy(Trophy, ConditionType.use_merat, [Goal], locked: true),
            TestGameSession.Trophy(OtherTrophy, ConditionType.use_merat, [Goal]));
        session.Currency.Meret = 100;
        test.TakeSent();

        session.Currency.Meret -= 40;
        Assert.That(test.TrophyCounter(Trophy), Is.Null);
        Assert.That(test.TrophyCounter(OtherTrophy), Is.EqualTo(40));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Meret, SendOp.Achieve }));
    }
    #endregion

    #region home_goto
    private HomeHandler HomeHandler() => new() { MapMetadataStorage = test.MapMetadata, World = test.World };

    private static Maple2.PacketLib.Tools.ByteReader Warp(int? template = null) => TestGameSession.Packet(writer => {
        writer.WriteByte(3);
        if (template != null) {
            writer.WriteInt(template.Value);
        }
    });

    private GameSession StartWithHome(params AchievementMetadata[] trophies) {
        GameSession session = Start(trophies);
        session.Player.Value.Home.Indoor.Name = "home";
        return session;
    }

    [Test]
    public void HomeWarp_CountsOnceBeforeTheMove() {
        GameSession session = StartWithHome(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [Goal]));

        HomeHandler().Handle(session, Warp());
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve, SendOp.GameToGame }));
        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
        Assert.That(test.World.Moves[0].MapId, Is.EqualTo(Constant.DefaultHomeMapId));
        Assert.That(test.World.Moves[0].OwnerId, Is.EqualTo(test.AccountId));
    }

    [Test]
    public void HomeWarp_RefusedByTheWorldCountsNothing() {
        GameSession session = StartWithHome(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [Goal]));
        test.World.MoveAddress = "";

        HomeHandler().Handle(session, Warp());
        Assert.That(test.TrophyCounter(Trophy), Is.Null);
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.GameToGame }));
        Assert.That(test.Errors, Has.Count.EqualTo(1));
    }

    [Test]
    public void HomeWarp_FromAMapThatForbidsItCountsNothing() {
        GameSession session = StartWithHome(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [Goal]));
        test.EnterMap(TestGameSession.Map(Town, homeReturnable: false));

        HomeHandler().Handle(session, Warp());
        Assert.That(test.TrophyCounter(Trophy), Is.Null);
        Assert.That(test.World.Moves, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Notice }));
    }

    [Test]
    public void HomeWarp_FromTheHomeCountsNothing() {
        GameSession session = StartWithHome(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [Goal]));
        test.EnterHome();

        HomeHandler().Handle(session, Warp());
        Assert.That(test.TrophyCounter(Trophy), Is.Null);
        Assert.That(test.World.Moves, Is.Empty);
    }

    [Test]
    public void HomeWarp_ToAMapThePlayedClientLacksCountsNothing() {
        GameSession session = StartWithHome(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [Goal]));
        test.AddMap(TestGameSession.Map(Constant.DefaultHomeMapId, notInPlayedClient: true));

        HomeHandler().Handle(session, Warp());
        Assert.That(test.TrophyCounter(Trophy), Is.Null);
        Assert.That(test.World.Moves, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Notice }));
    }

    // The first warp makes the home: its level 1 is the first interior_level report, then the move counts.
    [Test]
    public void HomeWarp_FirstTimeMakesTheHomeAndCountsBoth() {
        GameSession session = Start(
            TestGameSession.Trophy(Trophy, ConditionType.home_goto, [Goal]),
            TestGameSession.Trophy(OtherTrophy, ConditionType.interior_level, [Goal]));
        Assert.That(session.Player.Value.Home.Indoor.Name, Is.Empty);

        HomeHandler().Handle(session, Warp(template: -1));
        Assert.That(session.Player.Value.Home.Indoor.Name, Is.Not.Empty);
        Assert.That(test.TrophyCounter(OtherTrophy), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve, SendOp.Achieve, SendOp.GameToGame }));

        // The home exists now: a second warp reports no level again.
        HomeHandler().Handle(session, Warp());
        Assert.That(test.TrophyCounter(OtherTrophy), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(2));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve, SendOp.GameToGame }));
    }

    private QuestHandler GuideHandler() {
        test.SetTable(new LearningQuestTable(new Dictionary<int, LearningQuestTable.Entry> {
            [1] = new(0, 0, 0, 0, Constant.DefaultHomeMapId, 0),
        }));
        return new QuestHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata };
    }

    private static Maple2.PacketLib.Tools.ByteReader Guide(int id) => TestGameSession.Packet(writer => {
        writer.WriteByte(16);
        writer.WriteInt(id);
    });

    [Test]
    public void GuideGoHome_CountsFromElsewhere() {
        GameSession session = StartWithHome(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [Goal]));

        GuideHandler().Handle(session, Guide(1));
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve, SendOp.GameToGame }));
        Assert.That(test.World.Moves[0].OwnerId, Is.EqualTo(test.AccountId));
    }

    [Test]
    public void GuideGoHome_InTheOwnHomeCountsNothing() {
        GameSession session = StartWithHome(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [Goal]));
        test.EnterHome();

        GuideHandler().Handle(session, Guide(1));
        Assert.That(test.TrophyCounter(Trophy), Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.GameToGame }));
    }

    [Test]
    public void GuideGoHome_RefusedByTheWorldCountsNothing() {
        GameSession session = StartWithHome(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [Goal]));
        test.World.MoveAddress = "";

        GuideHandler().Handle(session, Guide(1));
        Assert.That(test.TrophyCounter(Trophy), Is.Null);
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.GameToGame }));
        Assert.That(test.Errors, Has.Count.EqualTo(1));
    }

    [Test]
    public void GuideOfAnUnknownRow_DoesNothing() {
        GameSession session = StartWithHome(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [Goal]));

        GuideHandler().Handle(session, Guide(2));
        Assert.That(test.TrophyCounter(Trophy), Is.Null);
        Assert.That(test.World.Moves, Is.Empty);
    }
    #endregion

    #region dungeon_first_clear
    private const int Dungeon = 90000200;

    private GameSession StartWithDungeon(DungeonGroupType group = DungeonGroupType.normal, int rewardCount = 10) {
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Dungeon, 90000201, rewardCount) with { GroupType = group };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Dungeon] = room }));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        GameSession session = Start(
            TestGameSession.Trophy(Trophy, ConditionType.dungeon_first_clear, [Goal], codes: [Dungeon]),
            TestGameSession.Trophy(OtherTrophy, ConditionType.dungeon_first_clear, [Goal], codes: [Dungeon + 1]));
        test.EnterDungeon(room).State = DungeonState.Clear;
        test.TakeSent();
        return session;
    }

    [Test]
    public void DungeonClear_FirstCountsAndSecondDoesNot() {
        GameSession session = StartWithDungeon();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        session.Dungeon.CompleteDungeon(now);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(OtherTrophy), Is.Null);
        List<SendOp> ops = Sent();
        // The trophy update follows the record update.
        Assert.That(ops[ops.IndexOf(SendOp.Achieve) - 1], Is.EqualTo(SendOp.RoomDungeon));
        Assert.That(ops.Count(op => op == SendOp.Achieve), Is.EqualTo(1));

        test.EnterDungeon(session.Dungeon.Metadata!).State = DungeonState.Clear;
        session.Dungeon.CompleteDungeon(now + 60);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(Sent(), Has.No.Member(SendOp.Achieve));
    }

    [Test]
    public void DungeonFail_CountsNothing() {
        GameSession session = StartWithDungeon();
        test.EnterDungeon(session.Dungeon.Metadata!).State = DungeonState.Fail;

        session.Dungeon.FailDungeon();
        Assert.That(test.TrophyCounter(Trophy), Is.Null);
        Assert.That(Sent(), Has.No.Member(SendOp.Achieve));
    }

    // A clear without a reward count left is not recorded as a clear, so it is not a first clear either.
    [Test]
    public void DungeonClear_WithoutARewardCountCountsNothing() {
        GameSession session = StartWithDungeon(rewardCount: 0);

        session.Dungeon.CompleteDungeon(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.That(test.TrophyCounter(Trophy), Is.Null);
        Assert.That(test.Errors, Is.Empty);
    }
    #endregion

    #region quest_revise_achieve
    private const int QuestId = 90000300;

    [Test]
    public void QuestComplete_CountsForTheQuestsOfTheCode() {
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        GameSession session = Start(TestGameSession.Trophy(Trophy, ConditionType.quest_revise_achieve, [Goal], codes: [QuestId, QuestId + 1]));
        Quest listed = test.StartQuest(TestGameSession.QuestData(QuestId));
        Quest unlisted = test.StartQuest(TestGameSession.QuestData(QuestId + 2));

        Assert.That(session.Quest.Complete(unlisted), Is.True);
        Assert.That(test.TrophyCounter(Trophy), Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Quest }));

        Assert.That(session.Quest.Complete(listed), Is.True);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        // The quest's own packet first, as KMS2 sends it.
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Quest, SendOp.Achieve }));

        // A completed quest is not completed again.
        Assert.That(session.Quest.Complete(listed), Is.False);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(Sent(), Is.Empty);
    }
    #endregion

    #region interior_level and interior_exp_offset
    private const int LevelTrophy = 90000401;
    private const int ExpTrophy = 90000402;

    private RequestCubeHandler CubeHandler() => new() { TableMetadata = test.TableMetadata };

    private static Maple2.PacketLib.Tools.ByteReader CheckIn() => TestGameSession.Packet(writer => writer.WriteByte(40));

    // A home at level 1 whose plot holds a bed (decoration score 50... see InteriorPointRules) when furnished.
    private (GameSession Session, Plot Plot) StartInHome(bool furnished) {
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry> {
            [100] = new(100, 0),
        }));
        // Level 2 at 40 exp, level 3 at 1000.
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> {
            [1] = new(1, 0, 0), [2] = new(2, 40, 0), [3] = new(3, 1000, 0),
        }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        GameSession session = Start(
            TestGameSession.Trophy(LevelTrophy, ConditionType.interior_level, [Goal]),
            TestGameSession.Trophy(ExpTrophy, ConditionType.interior_exp_offset, [Goal]));
        session.Housing.InitNewHome("home", null);
        Plot plot = test.EnterHome();
        if (furnished) {
            ItemMetadata bed = TestGameSession.Metadata(50100001) with { Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.Bed, false) };
            plot.Cubes[new Maple2.Model.Common.Vector3B(1, 1, 1)] = new PlotCube(bed, 1) { Type = PlotCube.CubeType.Default };
        }
        test.TakeSent();
        return (session, plot);
    }

    [Test]
    public void HomeCreation_ReportsLevelOne() {
        (GameSession _, Plot _) = StartInHome(furnished: false);
        Assert.That(test.TrophyCounter(LevelTrophy), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(ExpTrophy), Is.Null);
    }

    [Test]
    public void CheckIn_ReportsTheExpThenTheLevelOnceADay() {
        (GameSession session, Plot plot) = StartInHome(furnished: true);
        int score = Maple2.Server.Game.Manager.HousingManager.DecorationScore(plot);
        Assert.That(score, Is.GreaterThanOrEqualTo(40).And.LessThan(1000));

        CubeHandler().Handle(session, CheckIn());
        Assert.That(session.Player.Value.Home.DecorationLevel, Is.EqualTo(2));
        Assert.That(test.TrophyCounter(ExpTrophy), Is.EqualTo(score));
        Assert.That(test.TrophyCounter(LevelTrophy), Is.EqualTo(2));
        // The check-in answer, then the exp trophy, then the level trophy.
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.ResponseCube, SendOp.Achieve, SendOp.Achieve }));

        // The same day: no second check-in.
        CubeHandler().Handle(session, CheckIn());
        Assert.That(test.TrophyCounter(ExpTrophy), Is.EqualTo(score));
        Assert.That(test.TrophyCounter(LevelTrophy), Is.EqualTo(2));
        Assert.That(Sent(), Is.Empty);

        // The next day (the daily reset clears the timestamp): exp again, the level is unchanged and not sent.
        session.Player.Value.Home.DecorationRewardTimestamp = 0;
        CubeHandler().Handle(session, CheckIn());
        Assert.That(session.Player.Value.Home.DecorationLevel, Is.EqualTo(2));
        Assert.That(test.TrophyCounter(ExpTrophy), Is.EqualTo(score * 2));
        Assert.That(test.TrophyCounter(LevelTrophy), Is.EqualTo(2));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.ResponseCube, SendOp.Achieve }));
    }

    [Test]
    public void CheckIn_OfAnEmptyHomeCountsNoExp() {
        (GameSession session, Plot _) = StartInHome(furnished: false);

        CubeHandler().Handle(session, CheckIn());
        Assert.That(session.Player.Value.Home.DecorationRewardTimestamp, Is.Not.Zero);
        Assert.That(test.TrophyCounter(ExpTrophy), Is.Null);
        Assert.That(test.TrophyCounter(LevelTrophy), Is.EqualTo(1));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.ResponseCube }));
    }

    [Test]
    public void CheckIn_InAnotherPlayersPlotCountsNothing() {
        (GameSession session, Plot plot) = StartInHome(furnished: true);
        plot.OwnerId = test.AccountId + 1;

        CubeHandler().Handle(session, CheckIn());
        Assert.That(session.Player.Value.Home.DecorationRewardTimestamp, Is.Zero);
        Assert.That(test.TrophyCounter(ExpTrophy), Is.Null);
        Assert.That(test.TrophyCounter(LevelTrophy), Is.EqualTo(1));
    }

    [Test]
    public void CheckIn_OutsideTheHomeCountsNothing() {
        (GameSession session, Plot _) = StartInHome(furnished: true);
        test.EnterMap(TestGameSession.Map(Town));

        CubeHandler().Handle(session, CheckIn());
        Assert.That(session.Player.Value.Home.DecorationRewardTimestamp, Is.Zero);
        Assert.That(test.TrophyCounter(ExpTrophy), Is.Null);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void HomeName_EmptyIsRefused(string name) {
        (GameSession session, Plot _) = StartInHome(furnished: false);

        session.Housing.SetName(name);
        Assert.That(session.Player.Value.Home.Indoor.Name, Is.EqualTo("home"));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.ResponseCube }));
    }
    #endregion

    #region minigame_clear, play_rps
    [Test]
    public void MiniGameClear_CountsForTheTrophyOfItsField() {
        GameSession session = Start(
            TestGameSession.Trophy(Trophy, ConditionType.minigame_clear, [Goal], codes: [61000006]),
            TestGameSession.Trophy(OtherTrophy, ConditionType.minigame_clear, [Goal], codes: [61000004]));

        Maple2.Server.Game.Trigger.TriggerContext.ReportClear(session, 61000006);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(OtherTrophy), Is.Null.Or.EqualTo(0));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve }));

        // A player-opened mini-game field ends with the same action and is no code of these trophies.
        Maple2.Server.Game.Trigger.TriggerContext.ReportClear(session, 81000003);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(Sent(), Is.Empty);
    }

    [TestCase((byte) 1, ConditionType.play_rps_win)]
    [TestCase((byte) 2, ConditionType.play_rps_lose)]
    [TestCase((byte) 0, ConditionType.play_rps_draw)]
    public void RpsResult_CountsForTheTrophyThatNamesTheTicket(byte result, ConditionType type) {
        const int ticket = 20302424;
        ConditionType other = type == ConditionType.play_rps_win ? ConditionType.play_rps_lose : ConditionType.play_rps_win;
        GameSession session = Start(
            TestGameSession.Trophy(Trophy, type, [Goal], codes: [ticket]),
            TestGameSession.Trophy(OtherTrophy, other, [Goal], codes: [ticket]));

        MicrogameHandler.ReportResult(session, result, ticket);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(OtherTrophy), Is.Null.Or.EqualTo(0));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve }));

        // The ticket of another event's game is no code of this trophy.
        MicrogameHandler.ReportResult(session, result, ticket + 1);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(Sent(), Is.Empty);
    }
    #endregion

    #region npc_assist_bonus, skill_die, auto_fishing
    [Test]
    public void AssistBonus_CountsForTheTrophyOfItsBoss() {
        GameSession session = Start(
            TestGameSession.Trophy(Trophy, ConditionType.npc_assist_bonus, [Goal], codes: [23000013]),
            TestGameSession.Trophy(OtherTrophy, ConditionType.npc_assist_bonus, [Goal], codes: [23000018]));

        Maple2.Server.Game.Model.FieldNpc.ReportAssistBonus(session, 23000013);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(OtherTrophy), Is.Null.Or.EqualTo(0));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve }));

        // A boss no trophy names counts for none.
        Maple2.Server.Game.Model.FieldNpc.ReportAssistBonus(session, 29000142);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void SkillDie_CountsForTheTrophyThatNamesTheSkill() {
        GameSession session = Start(
            TestGameSession.Trophy(Trophy, ConditionType.skill_die, [Goal], codes: [50100394, 50100405, 50100406]),
            TestGameSession.Trophy(OtherTrophy, ConditionType.skill_die, [Goal], codes: [70000500]));

        session.ConditionUpdate(ConditionType.skill_die, codeLong: 50100405);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(OtherTrophy), Is.Null.Or.EqualTo(0));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve }));

        session.ConditionUpdate(ConditionType.skill_die, codeLong: 50100365);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(Sent(), Is.Empty);
    }

    [TestCase(500, 0, true)]
    [TestCase(500, 1, false)]
    [TestCase(0, 0, false)] // a second hit in the tick of the death
    public void KilledByHit_OnlyTheHitThatTakesTheLastHealth(long before, long after, bool expected) {
        Assert.That(Maple2.Server.Game.Model.FieldPlayer.KilledByHit(before, after), Is.EqualTo(expected));
    }

    [Test]
    public void AutoCatch_CountsForTheAutoFishingTrophy() {
        GameSession session = Start(TestGameSession.Trophy(Trophy, ConditionType.auto_fishing, [Goal]));

        Maple2.Server.Game.Manager.FishingManager.ReportAutoCatch(session, hasAutoFish: false);
        Assert.That(test.TrophyCounter(Trophy), Is.Null.Or.EqualTo(0));
        Assert.That(Sent(), Is.Empty);

        Maple2.Server.Game.Manager.FishingManager.ReportAutoCatch(session, hasAutoFish: true);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve }));
    }
    #endregion

    #region quest conditions: jump, fish
    private static QuestMetadata QuestWith(int id, ConditionType type, int[]? codes = null, int[]? targets = null) {
        var condition = new ConditionMetadata(type, 50,
            codes == null ? null : new ConditionMetadata.Parameters(Integers: codes),
            targets == null ? null : new ConditionMetadata.Parameters(Integers: targets));
        return TestGameSession.QuestData(id) with { Conditions = [condition] };
    }

    private static QuestMetadata QuestWith(int id, ConditionMetadata condition) =>
        TestGameSession.QuestData(id) with { Conditions = [condition] };

    private static Maple2.PacketLib.Tools.ByteReader Jump() => TestGameSession.Packet(writer => {
        writer.WriteByte(0); // Jump
        writer.WriteByte(0);
        writer.WriteInt(0);
        writer.WriteBytes(new byte[12]); // position
    });

    [Test]
    public void Jump_CountsForTheQuestOfItsMapOnly() {
        GameSession session = Start();
        Quest anywhere = test.StartQuest(QuestWith(QuestId, ConditionType.jump));
        Quest here = test.StartQuest(QuestWith(QuestId + 1, ConditionType.jump, codes: [Town]));
        Quest elsewhere = test.StartQuest(QuestWith(QuestId + 2, ConditionType.jump, codes: [Town + 1]));
        test.EnterMap(TestGameSession.Map(Town));

        new StateHandler().Handle(session, Jump());
        Assert.That(anywhere.Conditions[0].Counter, Is.EqualTo(1));
        Assert.That(here.Conditions[0].Counter, Is.EqualTo(1));
        Assert.That(elsewhere.Conditions[0].Counter, Is.Zero);
    }

    [TestCase(ConditionType.fish)]
    [TestCase(ConditionType.fish_big)]
    public void Fish_CountsForTheQuestOfItsMapOnly(ConditionType type) {
        const int fish = 10000001;
        GameSession session = Start();
        Quest anywhere = test.StartQuest(QuestWith(QuestId, type));
        Quest here = test.StartQuest(QuestWith(QuestId + 1, type, targets: [Town + 1]));
        // A map with a lower id than the map of the catch: it counted while the target was read as a minimum.
        Quest lower = test.StartQuest(QuestWith(QuestId + 2, type, targets: [Town]));
        Quest higher = test.StartQuest(QuestWith(QuestId + 3, type, targets: [Town + 2]));
        test.EnterMap(TestGameSession.Map(Town + 1));

        // As FishingManager.AddFish reports a catch.
        session.ConditionUpdate(type, codeLong: fish, targetLong: session.Field!.MapId);
        Assert.That(anywhere.Conditions[0].Counter, Is.EqualTo(1));
        Assert.That(here.Conditions[0].Counter, Is.EqualTo(1));
        Assert.That(lower.Conditions[0].Counter, Is.Zero);
        Assert.That(higher.Conditions[0].Counter, Is.Zero);
    }

    [Test]
    public void StayCube_CountsASecondOnTheMaterialOfTheQuest() {
        GameSession session = Start();
        Quest grass = test.StartQuest(QuestWith(QuestId, new ConditionMetadata(ConditionType.stay_cube, 60, null,
            new ConditionMetadata.Parameters(Strings: ["grass"]))));
        Quest snow = test.StartQuest(QuestWith(QuestId + 1, new ConditionMetadata(ConditionType.stay_cube, 60, null,
            new ConditionMetadata.Parameters(Strings: ["snow"]))));
        Quest here = test.StartQuest(QuestWith(QuestId + 2, ConditionType.stay_cube, codes: [Town]));
        Quest elsewhere = test.StartQuest(QuestWith(QuestId + 3, ConditionType.stay_cube, codes: [Town + 1]));
        test.EnterMap(TestGameSession.Map(Town));
        var id = new Maple2.Model.Metadata.FieldEntity.FieldEntityId(0, 0, string.Empty);
        var cube = new Maple2.Model.Metadata.FieldEntity.FieldBoxColliderEntity(id, default, default, 1f, default, default, false, false, MapAttribute.grass);

        // Standing still: the block under the player is looked up once per second, whatever the state.
        Maple2.Server.Game.Model.FieldPlayer.ReportStayCube(session, cube);
        Maple2.Server.Game.Model.FieldPlayer.ReportStayCube(session, cube);
        Assert.That(grass.Conditions[0].Counter, Is.EqualTo(2));
        Assert.That(snow.Conditions[0].Counter, Is.Zero);
        Assert.That(here.Conditions[0].Counter, Is.EqualTo(2));
        Assert.That(elsewhere.Conditions[0].Counter, Is.Zero);

        // No cube (in the air) and a fluid count nothing.
        Maple2.Server.Game.Model.FieldPlayer.ReportStayCube(session, null);
        Maple2.Server.Game.Model.FieldPlayer.ReportStayCube(session, cube with { IsFluid = true });
        Assert.That(grass.Conditions[0].Counter, Is.EqualTo(2));
    }

    [Test]
    public void InstrumentTime_CountsForTheCategoryOfTheInstrument() {
        GameSession session = Start();
        Quest piano = test.StartQuest(QuestWith(QuestId, new ConditionMetadata(ConditionType.music_play_instrument_time, 20,
            new ConditionMetadata.Parameters(Integers: [1]), new ConditionMetadata.Parameters(Integers: [Town]))));
        Quest any = test.StartQuest(QuestWith(QuestId + 1, ConditionType.music_play_instrument_time));
        test.EnterMap(TestGameSession.Map(Town));

        // Instrument 2 is of category 2: 3 seconds for the quest of any instrument only.
        InstrumentHandler.ReportPlayTime(session, new InstrumentMetadata(2, 0, 1, 2, 0, 0), 3500);
        Assert.That(piano.Conditions[0].Counter, Is.Zero);
        Assert.That(any.Conditions[0].Counter, Is.EqualTo(3));

        // Instrument 6 is of category 1.
        InstrumentHandler.ReportPlayTime(session, new InstrumentMetadata(6, 0, 3, 1, 0, 0), 5000);
        Assert.That(piano.Conditions[0].Counter, Is.EqualTo(5));
        Assert.That(any.Conditions[0].Counter, Is.EqualTo(8));
    }
    #endregion

    #region guild notice
    [Test]
    public void GradeEarned_IsToldToTheGuildWithTheLastGradeMarked() {
        GameSession session = Start(TestGameSession.Trophy(Trophy, ConditionType.jump, [1, 2]));
        session.Player.Value.Character.GuildId = 77;

        session.ConditionUpdate(ConditionType.jump);
        session.ConditionUpdate(ConditionType.jump);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(test.World.GuildRequests.Select(request => request.RequestorId), Is.All.EqualTo(session.CharacterId));
        Assert.That(test.World.GuildRequests.Select(request => (request.TrophyNotice.GuildId, request.TrophyNotice.TrophyId,
            request.TrophyNotice.Grade, request.TrophyNotice.LastGrade)), Is.EqualTo(new[] {
            (77L, Trophy, 1, false),
            (77L, Trophy, 2, true),
        }));
    }

    [Test]
    public void GradeEarned_WithoutAGuildTellsNobody() {
        GameSession session = Start(TestGameSession.Trophy(Trophy, ConditionType.jump, [1]));

        session.ConditionUpdate(ConditionType.jump);

        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1));
        Assert.That(test.World.GuildRequests, Is.Empty);
    }

    [Test]
    public void GuildNotice_KindIs3ForTheLastGradeAnd2Before() {
        byte[] progress = Maple2.Server.Game.Packets.GuildPacket.Achievement("a", Trophy, 1, false).ToArray();
        byte[] complete = Maple2.Server.Game.Packets.GuildPacket.Achievement("a", Trophy, 2, true).ToArray();
        // op, mode 35, name (length 1), trophy id, grade, kind
        Assert.That(progress.Length, Is.EqualTo(2 + 1 + 4 + 4 + 4 + 2));
        Assert.That(progress[2], Is.EqualTo(35));
        Assert.That(BitConverter.ToInt32(progress, 7), Is.EqualTo(Trophy));
        Assert.That(BitConverter.ToInt32(progress, 11), Is.EqualTo(1));
        Assert.That(BitConverter.ToInt16(progress, 15), Is.EqualTo(2));
        Assert.That(BitConverter.ToInt32(complete, 11), Is.EqualTo(2));
        Assert.That(BitConverter.ToInt16(complete, 15), Is.EqualTo(3));
    }
    #endregion
}

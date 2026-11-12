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
        Assert.That(Sent(), Is.EqualTo(new[] { SendOp.Achieve, SendOp.Quest }));

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
}

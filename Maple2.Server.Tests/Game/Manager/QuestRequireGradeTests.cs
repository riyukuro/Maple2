using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Manager;

// require alliance/fameGrade and masteryType/masteryGrade (CQuestManagerCommon::CanAcceptQuest, questdata.cpp:576):
// the client does not accept a quest above the player's grade, so CanStart refuses it, and so the alliance mission
// lists (AllianceMissions.Pick) and accept all leave it out.
public class QuestRequireGradeTests {
    private const AllianceType Alliance = AllianceType.KritiasMapleAlliance;
    private const int GradeTwoPoints = 1000;
    private const int HighMission = 91000140; // the tests set each mission's grade: fame grade 2 here
    private const int LowMission = 91000150;  // fame grade 1 here
    private const int SmithMission = 93000074; // smithing grade 2 here

    private TestGameSession test = null!;
    private GameSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        // A fixed count per alliance: one daily mission of rank A (1).
        var method = new FameTable.PickMethod(false, 2, [0, 1], 0, 0, [new FameTable.Alliance(Alliance, 0)]);
        test.SetTable(new FameTable([method], new Dictionary<int, AllianceType>(), GradeRows: [
            new FameTable.Grade(Alliance, 1, 0),
            new FameTable.Grade(Alliance, 2, GradeTwoPoints),
        ]));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>> {
            [MasteryType.Blacksmithing] = new Dictionary<int, MasteryRewardTable.Entry> {
                [1] = new(0, 0, 0, 0),
                [2] = new(500, 0, 0, 0),
            },
        }));
    }

    private static QuestMetadata Mission(int id, int fameGrade = 0, MasteryType masteryType = MasteryType.Unknown, int masteryGrade = 0) {
        QuestMetadata quest = TestGameSession.QuestData(id, QuestType.AllianceQuest);
        return quest with {
            Basic = quest.Basic with { Repeat = 2, Alliance = Alliance, Rank = 1 },
            Require = quest.Require with {
                FameAlliance = fameGrade == 0 ? AllianceType.AnyFaction : Alliance,
                FameGrade = fameGrade,
                MasteryType = masteryType,
                MasteryGrade = masteryGrade,
            },
        };
    }

    private void Login(params QuestMetadata[] missions) {
        test.Start();
        session = test.Session;
        foreach (QuestMetadata mission in missions) {
            test.Register(mission);
        }
        typeof(QuestManager).GetField("missionMetadata", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(session.Quest, missions.ToList());
    }

    // The ids of the day's list the login sends (Quest 0x1F).
    private int[] DailyList() {
        session.Quest.LoadAllianceMissions();
        byte[] expectedHead = QuestPacket.DailyAllianceMissions(false, Array.Empty<int>()).ToArray()[..4];
        byte[] packet = test.TakeSent().Last(sent => TestGameSession.Op(sent) == SendOp.Quest && sent.AsSpan(0, 4).SequenceEqual(expectedHead));
        int count = BitConverter.ToInt32(packet, 4);
        return Enumerable.Range(0, count).Select(i => BitConverter.ToInt32(packet, 8 + 4 * i)).Order().ToArray();
    }

    [TestCase(AllianceType.AnyFaction, 3, 1, true)]   // no alliance: nothing checked
    [TestCase(AllianceType.KritiasMapleAlliance, 0, 1, true)] // no grade: nothing checked
    [TestCase(AllianceType.KritiasMapleAlliance, 2, 1, false)]
    [TestCase(AllianceType.KritiasMapleAlliance, 2, 2, true)]
    [TestCase(AllianceType.KritiasMapleAlliance, 2, 5, true)]
    public void MeetsGrades_Fame(AllianceType alliance, int required, int grade, bool meets) {
        QuestMetadataRequire require = TestGameSession.QuestData(1).Require with { FameAlliance = alliance, FameGrade = required };
        Assert.That(QuestRules.MeetsGrades(require, _ => grade, _ => 0), Is.EqualTo(meets));
    }

    [TestCase(MasteryType.Unknown, 3, 1, true)]
    [TestCase(MasteryType.Cooking, 0, 1, true)]
    [TestCase(MasteryType.Cooking, 3, 2, false)]
    [TestCase(MasteryType.Cooking, 3, 3, true)]
    public void MeetsGrades_Mastery(MasteryType type, int required, int grade, bool meets) {
        QuestMetadataRequire require = TestGameSession.QuestData(1).Require with { MasteryType = type, MasteryGrade = required };
        Assert.That(QuestRules.MeetsGrades(require, _ => 0, _ => grade), Is.EqualTo(meets));
    }

    // The grade is the alliance's by its points (GetGradeByPoint): 999 is grade 1, 1000 grade 2.
    [TestCase(0, false)]
    [TestCase(GradeTwoPoints - 1, false)]
    [TestCase(GradeTwoPoints, true)]
    public void CanStart_FameGrade(int points, bool canStart) {
        Login(Mission(HighMission, fameGrade: 2));
        session.Player.Value.Unlock.FamePoints[Alliance] = points;

        Assert.That(session.Quest.CanStart(Mission(HighMission, fameGrade: 2)), Is.EqualTo(canStart));
    }

    // Another alliance's points do not count.
    [Test]
    public void CanStart_OtherAllianceFameDoesNotCount() {
        Login(Mission(HighMission, fameGrade: 2));
        session.Player.Value.Unlock.FamePoints[AllianceType.KritiasLumiknights] = GradeTwoPoints;

        Assert.That(session.Quest.CanStart(Mission(HighMission, fameGrade: 2)), Is.False);
    }

    [TestCase(499, false)]
    [TestCase(500, true)]
    public void CanStart_MasteryGrade(int exp, bool canStart) {
        Login(Mission(SmithMission, masteryType: MasteryType.Blacksmithing, masteryGrade: 2));
        session.Player.Value.Character.Mastery.Smithing = exp;

        Assert.That(session.Quest.CanStart(Mission(SmithMission, masteryType: MasteryType.Blacksmithing, masteryGrade: 2)), Is.EqualTo(canStart));
    }

    // A grade-1 player's list takes the grade-1 mission for the rank's one slot, whatever the shuffle.
    [Test]
    public void DailyList_LeavesOutMissionsAboveTheGrade() {
        Login(Mission(HighMission, fameGrade: 2), Mission(LowMission, fameGrade: 1));

        Assert.That(DailyList(), Is.EqualTo(new[] { LowMission }));
    }

    [Test]
    public void DailyList_GradeReachedListsBoth() {
        Login(Mission(HighMission, fameGrade: 2), Mission(LowMission, fameGrade: 1));
        session.Player.Value.Unlock.FamePoints[Alliance] = GradeTwoPoints;
        var method = new FameTable.PickMethod(false, 2, [0, 2], 0, 0, [new FameTable.Alliance(Alliance, 0)]);
        test.SetTable(session.TableMetadata.FameTable with { PickMethods = [method] });

        Assert.That(DailyList(), Is.EqualTo(new[] { HighMission, LowMission }));
    }

    // A mission picked before the check (or before a grade the player no longer has) and already started in the
    // period is held: the login keeps it in the list until the period ends, as the client's list is fixed for it,
    // and it fills the rank's slot. One not started is picked again and leaves the list, since the client would
    // not accept it.
    [Test]
    public void DailyList_HeldMissionAboveTheGradeStaysForThePeriod() {
        Login(Mission(HighMission, fameGrade: 2), Mission(LowMission, fameGrade: 1));
        Assume.That(session.Quest.Start(HighMission, bypassRequirements: true), Is.EqualTo(QuestError.none));

        Assert.That(DailyList(), Is.EqualTo(new[] { HighMission }));
        Assert.That(session.Quest.TryGetQuest(HighMission, out Quest? quest) && quest.State == QuestState.Started, Is.True);
    }

    // Accept all starts only what the list holds, so a mission above the grade is not started through it, and a
    // plain start of it is refused.
    [Test]
    public void AcceptAll_SkipsMissionsAboveTheGrade() {
        Login(Mission(HighMission, fameGrade: 2), Mission(LowMission, fameGrade: 1));
        DailyList();

        Assert.That(session.Quest.StartAllianceMissions(Alliance), Is.EqualTo(1));
        Assert.Multiple(() => {
            Assert.That(session.Quest.TryGetQuest(LowMission, out _), Is.True);
            Assert.That(session.Quest.TryGetQuest(HighMission, out _), Is.False);
            Assert.That(session.Quest.Start(HighMission), Is.Not.EqualTo(QuestError.none));
        });
    }
}

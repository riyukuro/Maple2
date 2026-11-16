using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// Achieve 5: the debug buttons of the trophy window (UIAchieveDialog onDbgUp / onDbgDown / onDbgUp30 / onDbgUp99,
// uiachievedialog.cpp:272-325): byte 5, int trophy id, byte button 0-3. GM only. Made-up trophy id.
public class TrophyDebugButtonTests {
    private const int Trophy = 90000022;

    private TestGameSession test = null!;

    private GameSession Start(bool gm) {
        test = new TestGameSession();
        test.AddAchievement(TestGameSession.Trophy(Trophy, ConditionType.killcount, [100, 200]));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        if (gm) {
            session.Player.AdminPermissions = AdminPermissions.GameMaster;
        }
        test.TakeSent();
        return session;
    }

    private static void Press(GameSession session, int id, byte button) {
        new AchievementHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteInt(id);
            writer.WriteByte(button);
        }));
    }

    [Test]
    public void NotGm_Dropped() {
        GameSession session = Start(gm: false);

        Press(session, Trophy, 0);

        Assert.Multiple(() => {
            Assert.That(session.Achievement.TryGetAchievement(Trophy, out _), Is.False);
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    // Up30 and up99 set the counter to 30 % and 99 % of the grade's goal on a trophy not held yet (it is created at
    // its lowest grade); a lower value never lowers the counter.
    [Test]
    public void Up30ThenUp99_SetTheCounterOfTheGrade() {
        GameSession session = Start(gm: true);

        Press(session, Trophy, 3);
        session.Achievement.TryGetAchievement(Trophy, out Achievement? trophy);
        long after99 = trophy!.Counter;
        Press(session, Trophy, 2);

        Assert.Multiple(() => {
            Assert.That(after99, Is.EqualTo(99));
            Assert.That(trophy.Counter, Is.EqualTo(99));
            Assert.That(trophy.CurrentGrade, Is.EqualTo(1));
            Assert.That(trophy.Grades, Is.Empty);
        });
    }

    // Up finishes the grade in progress; down takes the last finished grade back.
    [Test]
    public void UpThenDown_OneGradeForwardAndBack() {
        GameSession session = Start(gm: true);

        Press(session, Trophy, 0);
        session.Achievement.TryGetAchievement(Trophy, out Achievement? trophy);
        int[] afterUp = trophy!.Grades.Keys.ToArray();
        int gradeAfterUp = trophy.CurrentGrade;
        Press(session, Trophy, 1);

        Assert.Multiple(() => {
            Assert.That(afterUp, Is.EqualTo(new[] { 1 }));
            Assert.That(gradeAfterUp, Is.EqualTo(2));
            Assert.That(trophy.Grades, Is.Empty);
            Assert.That(trophy.CurrentGrade, Is.EqualTo(1));
            Assert.That(trophy.Counter, Is.EqualTo(0));
        });
    }

    // Down on a trophy not held, an unknown trophy and a button past 3 change nothing.
    [TestCase(Trophy, (byte) 1)]
    [TestCase(12345, (byte) 0)]
    [TestCase(Trophy, (byte) 4)]
    public void NothingToDo_NothingCreated(int id, byte button) {
        GameSession session = Start(gm: true);

        Press(session, id, button);

        Assert.That(session.Achievement.TryGetAchievement(id, out _), Is.False);
    }
}

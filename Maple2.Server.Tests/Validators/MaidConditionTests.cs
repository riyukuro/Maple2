using System.Collections.Generic;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Validators;

// The maid fields of npcScriptCondition (maid 11000512): maid_mood_time / maid_affinity_time (1800 seconds since the
// last talk of the kind), maid_ready_to_pay (the last MaidReadyToPay days, KR 7), maid_day_before_expired (3) and
// maid_affinity_grade (level at least the grade).
public class MaidConditionTests {
    private const long Now = 1_800_000_000;
    private const long Day = 24 * 60 * 60;

    private static Maid Maid(long moodTime = Now, long affinityTime = Now, long expiry = Now + 30 * Day, int level = 1) =>
        new() { Uid = 1, OwnerId = 5, ItemId = 1, MaidId = 1, CreationTime = Now, ExpiryTime = expiry, MoodTime = moodTime, AffinityTime = affinityTime, Level = level };

    private static ScriptConditionMetadata.MaidData Condition(KeyValuePair<int, bool> mood = default, KeyValuePair<int, bool> affinity = default,
            bool? readyToPay = null, KeyValuePair<int, bool> daysBefore = default, int grade = 0) =>
        new(true, null, readyToPay, grade, affinity, mood, daysBefore);

    private static KeyValuePair<int, bool> Kv(int key, bool value) => new(key, value);

    // CMaid::IsTime38Over: strictly more than the seconds.
    [TestCase(1800, ExpectedResult = false)]
    [TestCase(1801, ExpectedResult = true)]
    public bool IsTimeOver_IsStrict(long elapsed) => MaidRules.IsTimeOver(Now - elapsed, 1800, Now);

    [Test]
    public void MoodTime_OpensTheMoodTalk_AfterHalfAnHour() {
        Maid fresh = Maid(moodTime: Now - 60), rested = Maid(moodTime: Now - 3600);
        Assert.Multiple(() => {
            Assert.That(NpcTalkUtil.MaidMatches(Condition(mood: Kv(1800, true)), fresh, Now, 7), Is.False);
            Assert.That(NpcTalkUtil.MaidMatches(Condition(mood: Kv(1800, false)), fresh, Now, 7), Is.True);
            Assert.That(NpcTalkUtil.MaidMatches(Condition(mood: Kv(1800, true)), rested, Now, 7), Is.True);
            // The affinity time is its own: a rested mood does not open the affinity talk.
            Assert.That(NpcTalkUtil.MaidMatches(Condition(affinity: Kv(1800, true)), rested, Now, 7), Is.False);
            Assert.That(NpcTalkUtil.MaidMatches(Condition(affinity: Kv(1800, true)), Maid(affinityTime: Now - 3600), Now, 7), Is.True);
        });
    }

    [Test]
    public void ReadyToPay_InTheLastDays_AndAfterTheEnd() {
        Assert.Multiple(() => {
            Assert.That(MaidRules.IsReadyToPay(Maid(expiry: Now + 30 * Day), Now, 7), Is.False);
            Assert.That(MaidRules.IsReadyToPay(Maid(expiry: Now + 8 * Day), Now, 7), Is.False);
            Assert.That(MaidRules.IsReadyToPay(Maid(expiry: Now + 7 * Day + 100), Now, 7), Is.True);
            Assert.That(MaidRules.IsReadyToPay(Maid(expiry: Now - Day), Now, 7), Is.True);
            Assert.That(NpcTalkUtil.MaidMatches(Condition(readyToPay: false), Maid(), Now, 7), Is.True);
            Assert.That(NpcTalkUtil.MaidMatches(Condition(readyToPay: true), Maid(), Now, 7), Is.False);
        });
    }

    [Test]
    public void DaysBeforeExpired_TheLastThreeDays() {
        Assert.Multiple(() => {
            Assert.That(NpcTalkUtil.MaidMatches(Condition(daysBefore: Kv(3, true)), Maid(expiry: Now + 3 * Day + 100), Now, 7), Is.True);
            Assert.That(NpcTalkUtil.MaidMatches(Condition(daysBefore: Kv(3, true)), Maid(expiry: Now + 4 * Day), Now, 7), Is.False);
            Assert.That(NpcTalkUtil.MaidMatches(Condition(daysBefore: Kv(3, false)), Maid(expiry: Now + 4 * Day), Now, 7), Is.True);
        });
    }

    [TestCase(1, 1, ExpectedResult = true)]
    [TestCase(3, 1, ExpectedResult = false)]
    [TestCase(3, 5, ExpectedResult = true)]
    [TestCase(6, 5, ExpectedResult = false)]
    public bool AffinityGrade_IsAMinimumLevel(int grade, int level) => NpcTalkUtil.MaidMatches(Condition(grade: grade), Maid(level: level), Now, 7);

    // A condition with a maid field fails for a npc with no placed maid; one without them passes.
    [Test]
    public void NoMaid_FailsEachCheck_ButAnEmptyConditionPasses() {
        Assert.Multiple(() => {
            Assert.That(NpcTalkUtil.MaidMatches(Condition(), null, Now, 7), Is.True);
            Assert.That(NpcTalkUtil.MaidMatches(Condition(mood: Kv(1800, false)), null, Now, 7), Is.False);
            Assert.That(NpcTalkUtil.MaidMatches(Condition(readyToPay: false), null, Now, 7), Is.False);
            Assert.That(NpcTalkUtil.MaidMatches(Condition(daysBefore: Kv(3, false)), null, Now, 7), Is.False);
            Assert.That(NpcTalkUtil.MaidMatches(Condition(grade: 1), null, Now, 7), Is.False);
        });
    }

    // A new maid starts both waits (a new CMaid sets both times to the server time).
    [Test]
    public void Hire_SetsBothTalkTimesToNow() {
        var cube = new PlotCube(TestGameSession.Metadata(50400005), id: 77) { Type = PlotCube.CubeType.Construction };
        Maid maid = MaidRules.Hire(cube, 1, 5, Now, 30);
        Assert.Multiple(() => {
            Assert.That(maid.MoodTime, Is.EqualTo(Now));
            Assert.That(maid.AffinityTime, Is.EqualTo(Now));
        });
    }
}

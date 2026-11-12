using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class TrophyTimeRulesTests {
    // Play time: one count for each full minute, the rest carried on.
    [Test]
    public void PlayTimeCountsFullMinutes() {
        long last = 1_000;
        Assert.That(TrophyTimeRules.PlayMinutes(ref last, 60_999), Is.EqualTo(0));
        Assert.That(last, Is.EqualTo(1_000));
        Assert.That(TrophyTimeRules.PlayMinutes(ref last, 61_000), Is.EqualTo(1));
        Assert.That(last, Is.EqualTo(61_000));
        Assert.That(TrophyTimeRules.PlayMinutes(ref last, 61_000 + 150_000), Is.EqualTo(2));
        Assert.That(last, Is.EqualTo(181_000));
    }

    // Emotion time: nothing below five seconds, then every due second; the rest of a second is kept.
    [Test]
    public void EmotionTimeCountsInStepsOfFiveOrMore() {
        long ms = 0;
        Assert.That(TrophyTimeRules.EmotionSeconds(ref ms, 4_999), Is.EqualTo(0));
        Assert.That(TrophyTimeRules.EmotionSeconds(ref ms, 1), Is.EqualTo(5));
        Assert.That(ms, Is.EqualTo(0));
        Assert.That(TrophyTimeRules.EmotionSeconds(ref ms, 6_500), Is.EqualTo(6));
        Assert.That(ms, Is.EqualTo(500));
        Assert.That(TrophyTimeRules.EmotionSeconds(ref ms, 4_400), Is.EqualTo(0));
        Assert.That(TrophyTimeRules.EmotionSeconds(ref ms, 100), Is.EqualTo(5));
        Assert.That(TrophyTimeRules.EmotionSeconds(ref ms, -50), Is.EqualTo(0));
    }

    [Test]
    public void EmotionSkillComesFromAnEmotionState() {
        var sit = new StateSync { State = ActorState.EmotionIdle, Flags = StateSync.Flag.Flag1, Flag1Unknown = 90200041 };
        Assert.That(TrophyTimeRules.EmotionSkillId(sit), Is.EqualTo(90200041));
        sit.State = ActorState.Emotion;
        Assert.That(TrophyTimeRules.EmotionSkillId(sit), Is.EqualTo(90200041));
        sit.State = ActorState.Walk;
        Assert.That(TrophyTimeRules.EmotionSkillId(sit), Is.EqualTo(0));
        var idle = new StateSync { State = ActorState.EmotionIdle };
        Assert.That(TrophyTimeRules.EmotionSkillId(idle), Is.EqualTo(0));
    }

    // Achieve 1 (load): the int after the trophy id is the trophy's current grade, not a constant.
    [Test]
    public void LoadSendsTheCurrentGrade() {
        var grades = new Dictionary<int, AchievementMetadataGrade>();
        foreach ((int grade, long value) in new[] { (1, 10L), (2, 50L), (3, 100L) }) {
            grades[grade] = new AchievementMetadataGrade(grade, new ConditionMetadata(ConditionType.playtime, value, null, null), null);
        }
        var achievement = new Achievement(new AchievementMetadata(23100001, null, false, AchievementCategory.Life, [], grades)) {
            CurrentGrade = 3,
            RewardGrade = 3,
            Counter = 60,
        };

        ByteWriter writer = AchievementPacket.Load([achievement]);
        var reader = new ByteReader(writer.ToArray());
        reader.ReadShort(); // opcode
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(23100001));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
    }
}

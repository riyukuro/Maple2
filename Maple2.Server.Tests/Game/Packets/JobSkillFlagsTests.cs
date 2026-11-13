using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Config;

namespace Maple2.Server.Tests.Game.Packets;

public class JobSkillFlagsTests {
    // Client: JobSkill::Read (guess/jobsystem.cpp:129) reads, after the skill id, the int level, then the bools changed,
    // learned and extra. A changed skill gets the "learned skill" notice; an extra skill is left out of the skill tree.
    private static (int Id, int Level, bool Changed, bool Learned, bool Extra) Write(SkillInfo.Skill skill) {
        var writer = new ByteWriter();
        skill.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);
        return (reader.ReadInt(), reader.ReadInt(), reader.ReadBool(), reader.ReadBool(), reader.ReadBool());
    }

    [Test]
    public void ASkillLearnedNowIsChangedOnceAndNeverExtra() {
        var skill = new SkillInfo.Skill(10200011, 0, 10);
        skill.SetLevel(1);

        Assert.That(Write(skill), Is.EqualTo((10200011, 1, true, true, false)));
        Assert.That(Write(skill), Is.EqualTo((10200011, 1, false, true, false)));
    }

    [Test]
    public void ASkillNotLearnedIsNeitherChangedNorLearned() {
        var skill = new SkillInfo.Skill(10200011, 0, 10);

        Assert.That(Write(skill), Is.EqualTo((10200011, 1, false, false, false)));
    }

    [Test]
    public void ARaisedLevelIsNotChanged() {
        var skill = new SkillInfo.Skill(10200011, 1, 10);
        skill.SetLevel(4);

        Assert.That(Write(skill), Is.EqualTo((10200011, 4, false, true, false)));
    }
}

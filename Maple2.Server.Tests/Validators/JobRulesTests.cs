using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class JobRulesTests {
    [TestCase(0, SkillRank.Both)]
    [TestCase(1, SkillRank.Basic)]
    [TestCase(2, SkillRank.Awakening)]
    public void ResetRankBytes(int clientRank, SkillRank expected) {
        var writer = new ByteWriter();
        writer.WriteInt(clientRank);
        Assert.That(JobRules.ReadResetRank(new ByteReader(writer.ToArray(), 0)), Is.EqualTo(expected));
    }

    [TestCase(-1)]
    [TestCase(3)]
    public void ResetRankRefused(int clientRank) {
        Assert.That(JobRules.ResetRank(clientRank), Is.Null);
    }
}

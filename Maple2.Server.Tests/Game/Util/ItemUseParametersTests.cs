using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class ItemUseParametersTests {
    [Test]
    public void QuestStart_ReadsQuestIdAndLevel() {
        Assert.That(ItemUseParameters.TryParseQuestStart("30000414", out int questId, out short level), Is.True);
        Assert.That((questId, level), Is.EqualTo((30000414, (short) 10)));

        Assert.That(ItemUseParameters.TryParseQuestStart("30000415,50", out questId, out level), Is.True);
        Assert.That((questId, level), Is.EqualTo((30000415, (short) 50)));

        // A 0 level is the default, as in the client.
        Assert.That(ItemUseParameters.TryParseQuestStart("30000415,0", out _, out level), Is.True);
        Assert.That(level, Is.EqualTo((short) 10));

        Assert.That(ItemUseParameters.TryParseQuestStart("", out _, out _), Is.False);
        Assert.That(ItemUseParameters.TryParseQuestStart(null, out _, out _), Is.False);
        Assert.That(ItemUseParameters.TryParseQuestStart("0", out _, out _), Is.False);
        Assert.That(ItemUseParameters.TryParseQuestStart("abc", out _, out _), Is.False);
    }

    [Test]
    public void ParseIntArray_SkipsWhatIsNotANumber() {
        Assert.That(ItemUseParameters.ParseIntArray("330000961, 330000962"), Is.EqualTo(new[] { 330000961, 330000962 }));
        Assert.That(ItemUseParameters.ParseIntArray("1,x,3"), Is.EqualTo(new[] { 1, 3 }));
        Assert.That(ItemUseParameters.ParseIntArray(null), Is.Empty);
    }

    [Test]
    public void BoxCooldown_AllowsTheClientsNextSend() {
        // 3 s cooldown: the client sends again 3000 ms after its last send.
        Assert.That(ItemBoxManager.IsCooldownOver(10_000, 13_000, 3), Is.True);
        Assert.That(ItemBoxManager.IsCooldownOver(10_000, 12_000, 3), Is.False);
        // A use that arrives a little early because the first one was delayed still passes.
        Assert.That(ItemBoxManager.IsCooldownOver(10_000, 13_000 - ItemBoxManager.CooldownSlackMs, 3), Is.True);
        Assert.That(ItemBoxManager.IsCooldownOver(10_000, 13_000 - ItemBoxManager.CooldownSlackMs - 1, 3), Is.False);
    }
}

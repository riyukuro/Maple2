using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// Emote Use of the two cheer emotions: taken on the performance map and from a caught Hide and Seek hider only,
// learned or not; refused everywhere else, even when learned. Other emotions must be learned.
public class CheerEmoteTests {
    [TestCase(EmoteHandler.ApplaudEmoteId)]
    [TestCase(EmoteHandler.GlowstickEmoteId)]
    public void CheerOnlyOnStageOrWhenCaught(int emoteId) {
        Assert.Multiple(() => {
            Assert.That(EmoteHandler.UseAllowed(emoteId, false, Constant.PerformanceMapId, false), Is.True);
            Assert.That(EmoteHandler.UseAllowed(emoteId, false, 2000062, true), Is.True);
            Assert.That(EmoteHandler.UseAllowed(emoteId, true, 2000062, false), Is.False);
            Assert.That(EmoteHandler.UseAllowed(emoteId, false, 2000062, false), Is.False);
        });
    }

    [Test]
    public void OtherEmotionsMustBeLearned() {
        Assert.Multiple(() => {
            Assert.That(EmoteHandler.UseAllowed(90200001, true, 2000062, false), Is.True);
            Assert.That(EmoteHandler.UseAllowed(90200001, false, Constant.PerformanceMapId, true), Is.False);
        });
    }
}

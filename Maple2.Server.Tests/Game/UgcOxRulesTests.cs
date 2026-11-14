using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The home OX quiz event: HomeAction 10 read the way the client writes it.
public class UgcOxRulesTests {
    [Test]
    public void CreateSettingsAreFeeMaxShare() {
        // UIQuizOXCreateDialog::OnCreate: the fee combo's data (an __int64), then the most entrants and the winners'
        // share, after the command byte 10 and the sub-type 0 the handler has read.
        var writer = new ByteWriter();
        writer.WriteLong(5000);
        writer.WriteInt(20);
        writer.WriteInt(90);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 0);

        UgcOxRules.Settings settings = UgcOxRules.ReadSettings(reader);

        Assert.That(settings, Is.EqualTo(new UgcOxRules.Settings(5000, 20, 90)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

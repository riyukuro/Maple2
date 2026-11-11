using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class MyInfoRulesTests {
    [Test]
    public void MottoBytes() {
        var writer = new ByteWriter();
        writer.WriteUnicodeString("hello");
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(MyInfoRules.ReadMotto(reader), Is.EqualTo("hello"));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Banned() {
        Assert.That(MyInfoRules.MottoBanned("a bad day", text => text.Contains("bad")), Is.True);
        Assert.That(MyInfoRules.MottoBanned("a good day", text => text.Contains("bad")), Is.False);
        // An empty motto clears the text and is not looked up.
        Assert.That(MyInfoRules.MottoBanned("", _ => true), Is.False);
    }
}

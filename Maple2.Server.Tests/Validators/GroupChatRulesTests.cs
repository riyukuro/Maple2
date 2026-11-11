using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class GroupChatRulesTests {
    [Test]
    public void InviteBytes() {
        var writer = new ByteWriter();
        writer.WriteUnicodeString("Friend");
        writer.WriteInt(77);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(GroupChatRules.ReadInvite(reader), Is.EqualTo(("Friend", 77)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void LeaveBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(77);
        writer.WriteByte(1);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(GroupChatRules.ReadLeave(reader), Is.EqualTo((77, true)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void ChatBytes() {
        var writer = new ByteWriter();
        writer.WriteUnicodeString("hello");
        writer.WriteInt(77);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(GroupChatRules.ReadChat(reader), Is.EqualTo(("hello", 77)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Lengths() {
        Assert.That(GroupChatRules.NameValid(""), Is.True);
        Assert.That(GroupChatRules.NameValid(new string('a', 24)), Is.True);
        Assert.That(GroupChatRules.NameValid(new string('a', 25)), Is.False);
        Assert.That(GroupChatRules.TextValid(""), Is.False);
        Assert.That(GroupChatRules.TextValid(new string('a', 2048)), Is.True);
        Assert.That(GroupChatRules.TextValid(new string('a', 2049)), Is.False);
    }
}

using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game;

// The motto request of the character info window (RecvOp.MyInfo): a motto is stored on the character and flagged for
// the next sync; one that is too long or banned is refused with the client's own notice and changes nothing.
public class MyInfoHandlerTests {
    private TestGameSession test = null!;
    private MyInfoHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        handler = new MyInfoHandler { BanWordStorage = TestGameSession.BanWords("badword") };
        test.Session.Player.Value.Character.Motto = "old";
        test.TakeSent();
    }

    private void SetMotto(string motto) {
        handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteUnicodeString(motto);
        }));
    }

    [Test]
    public void SetMotto_StoresItAndFlagsThePlayer() {
        SetMotto("hello there");

        Assert.That(test.Session.Player.Value.Character.Motto, Is.EqualTo("hello there"));
        Assert.That(test.Session.Player.Flag.HasFlag(PlayerObjectFlag.Motto), Is.True);
        Assert.That(test.TakeSent(), Is.EqualTo(new List<byte[]> { MyInfoPacket.UpdateMotto(test.Session.Player).ToArray() }));
    }

    [Test]
    public void SetMotto_TooLongIsRefusedWithTheNotice() {
        string motto = new('a', 21);

        SetMotto(motto);

        Assert.That(test.Session.Player.Value.Character.Motto, Is.EqualTo("old"));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0], Is.EqualTo(MyInfoPacket.Error(test.Session.Player.ObjectId, "The motto can only be up to 20 letters long.").ToArray()));
    }

    [Test]
    public void SetMotto_TwentyLettersAreAllowed() {
        SetMotto(new string('a', 20));

        Assert.That(test.Session.Player.Value.Character.Motto, Has.Length.EqualTo(20));
    }

    [Test]
    public void SetMotto_BannedWordIsRefusedWithTheBanNotice() {
        SetMotto("a badword here");

        Assert.That(test.Session.Player.Value.Character.Motto, Is.EqualTo("old"));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0], Is.EqualTo(MyInfoPacket.BanMotto(test.Session.Player.ObjectId, "a badword here").ToArray()));
        Assert.That(test.Session.Player.Flag.HasFlag(PlayerObjectFlag.Motto), Is.False);
    }
}

using System;
using System.Linq;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Login;

public class RequestServerListTests {
    // The client leaves the character list: the account's login entry goes, so the login on its next connection is
    // not refused as already logged in. Nothing is sent back.
    [Test]
    public void LeavingTheCharacterListReleasesTheAccount() {
        var test = new TestLoginSession();
        LoginSession session = test.Start();
        Assert.That(session.Server.GetSession(test.AccountId, out LoginSession? before), Is.True);
        Assert.That(before, Is.SameAs(session));

        new RequestServerListHandler().Handle(session, TestGameSession.Packet(_ => { }));

        Assert.Multiple(() => {
            Assert.That(session.Server.GetSession(test.AccountId, out _), Is.False);
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A release names the session it was given: an entry of a later login of the same account stays.
    [Test]
    public void AnOlderSessionDoesNotReleaseALaterLogin() {
        var test = new TestLoginSession();
        LoginSession later = test.Start();
        var older = new TestLoginSession();
        LoginSession stale = older.Start(init: false);

        later.Server.Release(stale);

        Assert.That(later.Server.GetSession(test.AccountId, out LoginSession? current), Is.True);
        Assert.That(current, Is.SameAs(later));
    }

    // The server list's per-server byte is the account's character count (the list's charCnt), not a constant.
    [Test]
    public void TheServerListCountsTheAccountsCharacters() {
        var test = new TestLoginSession();
        test.AddCharacter("Second");
        test.AddCharacter("Other", accountId: test.AccountId + 1000);
        LoginSession session = test.Start();

        session.ListServers(test.AccountId);

        byte[] list = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.ServerList);
        var reader = new ByteReader(list, 2);
        Assert.Multiple(() => {
            Assert.That(reader.ReadString(), Is.EqualTo("dev"));
            Assert.That(reader.ReadByte(), Is.EqualTo(1)); // one server
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo(Target.SERVER_NAME));
            Assert.That(reader.ReadByte(), Is.EqualTo(2));
        });
    }

    [Test]
    public void TheCountIsClampedToAByte() {
        ByteWriter writer = ServerListPacket.Load("s", [], [1], 300);
        var reader = new ByteReader(writer.Buffer[..writer.Length], 2);
        reader.ReadString();
        reader.ReadByte();
        reader.ReadInt();
        reader.ReadUnicodeString();
        Assert.That(reader.ReadByte(), Is.EqualTo(byte.MaxValue));
    }
}

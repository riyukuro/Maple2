using System;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

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

    private static bool Ended(LoginSession session) =>
        (int) typeof(Maple2.Server.Core.Network.Session).GetField("disconnecting", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)! == 1;

    // A session that left the character list acts for the account no more: the client keeps the socket open on the
    // server list, and a login of the account from elsewhere gets in meanwhile (its entry was released), so a
    // delete (or a create) on the old socket would run next to that login, or delete the character it plays. The
    // client sends none from the server list; such a packet cuts the session and changes nothing.
    [Test]
    public void ADeleteAfterLeavingTheCharacterListIsRefused() {
        var test = new TestLoginSession();
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var handler = new CharacterManagementHandler {
            World = test.World, GameStorage = test.Storage, BanWordStorage = new BanWordStorage(words),
            ItemMetadata = null!, TableMetadata = test.Rows.TableMetadata, ServerTableMetadata = test.Rows.ServerTableMetadata,
        };
        LoginSession session = test.Start();
        session.ListCharacters();
        new RequestServerListHandler().Handle(session, TestGameSession.Packet(_ => { }));
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(test.CharacterId);
        }));

        using GameStorage.Request db = test.Storage.Context();
        Assert.Multiple(() => {
            Assert.That(db.GetCharacter(test.CharacterId, test.AccountId), Is.Not.Null);
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(Ended(session), Is.True);
        });
    }

    // Two logins of one account at the same moment both pass the login's check before either is entered: the later
    // entry cuts the earlier session, so the account has one character list.
    [Test]
    public void ALaterEntryCutsTheEarlierSession() {
        var test = new TestLoginSession();
        LoginSession first = test.Start();
        var other = new TestLoginSession();
        LoginSession second = other.Start(init: false);
        TestLoginSession.SetField(second, nameof(LoginSession.Server), first.Server);

        second.Init(test.AccountId, Guid.NewGuid());

        Assert.Multiple(() => {
            Assert.That(first.Server.GetSession(test.AccountId, out LoginSession? current), Is.True);
            Assert.That(current, Is.SameAs(second));
            Assert.That((Ended(first), Ended(second)), Is.EqualTo((true, false)));
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

    // The server's traffic is the characters online over what its channels hold, rounded up, so the list's
    // ServerRenderer draws comfort (0), normal (1-25), busy (26-90) or full (above 90). It was a fixed 100: full.
    [TestCase(0, 1, 200, 0)]
    [TestCase(1, 1, 200, 1)]
    [TestCase(50, 1, 200, 25)]
    [TestCase(51, 1, 200, 26)]
    [TestCase(180, 1, 200, 90)]
    [TestCase(181, 1, 200, 91)]
    [TestCase(100, 2, 200, 25)]
    [TestCase(5000, 1, 200, 100)]
    [TestCase(10, 0, 200, 0)]
    public void TheTrafficIsTheOnlineCountOverTheCapacity(int online, int channels, int capacity, int expected) {
        Assert.That(ServerListPacket.TrafficPercent(online, channels, capacity), Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(3)]
    public void TheServerListSendsTheWorldsTraffic(int online) {
        var test = new TestLoginSession();
        test.World.Online = online;
        LoginSession session = test.Start();

        session.ListServers(test.AccountId);

        byte[] list = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.ServerList);
        var reader = new ByteReader(list, 2);
        reader.ReadString();
        reader.ReadByte();
        reader.ReadInt();
        reader.ReadUnicodeString();
        reader.ReadByte();
        short addresses = reader.ReadShort();
        for (int i = 0; i < addresses; i++) {
            reader.ReadUnicodeString();
            reader.ReadShort();
        }
        Assert.That(reader.ReadInt(), Is.EqualTo(ServerListPacket.TrafficPercent(online, 1, Target.ChannelCapacity)));
    }
}

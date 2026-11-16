using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// HomeEntry.TryEnter, the one check of every way into a home (the visit request, a plot's door, a friend-home
// portal): the owner always enters; another player not if the owner blocked the account, not into a private home,
// and into a home with a password only with it typed. Each refusal sends the client's own line.
public class HomeEntryTests {
    private const long OwnerAccount = 900;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void Visitor() {
        test = new TestGameSession();
        test.Start();
        test.TakeSent();
    }

    private static Home HomeOf(long accountId, string passcode) => new() { AccountId = accountId, Passcode = passcode };

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void TheOwner_EntersEvenAPrivateHome() {
        Assert.That(HomeEntry.TryEnter(session, HomeOf(session.AccountId, HomeOpenRules.PrivatePasscode), ""), Is.True);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void AnOpenHome_LetsAVisitorIn() {
        Assert.That(HomeEntry.TryEnter(session, HomeOf(OwnerAccount, ""), ""), Is.True);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // The owner blocked one of the visitor's characters: the account is kept out, password or not.
    [Test]
    public void ABlockedVisitor_IsKeptOut() {
        using (GameStorage.Request db = test.Storage.Context()) {
            var alt = new Character { AccountId = session.AccountId, Name = "alt", Mastery = new Mastery() };
            alt.ReturnMaps.Push(2000062);
            Character? character = db.CreateCharacter(alt);
            Assert.That(db.CreateHomeBlock(OwnerAccount, character!.Id, ""), Is.Not.Null);
        }

        Assert.That(HomeEntry.TryEnter(session, HomeOf(OwnerAccount, "1234"), "1234"), Is.False);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(HomeBlockPacket.Result(HomeBlockPacket.Error.EntranceBlocked)) }));
    }

    [Test]
    public void APrivateHome_KeepsAVisitorOut() {
        Assert.That(HomeEntry.TryEnter(session, HomeOf(OwnerAccount, HomeOpenRules.PrivatePasscode), ""), Is.False);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(EnterUgcMapPacket.Private()) }));
    }

    // Nothing typed: the password box, whose answer comes back as a visit request.
    [Test]
    public void APasswordHome_AsksForThePassword() {
        Assert.That(HomeEntry.TryEnter(session, HomeOf(OwnerAccount, "1234"), ""), Is.False);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(EnterUgcMapPacket.RequestPassword(OwnerAccount)) }));
    }

    [Test]
    public void APasswordHome_RefusesAWrongPassword() {
        Assert.That(HomeEntry.TryEnter(session, HomeOf(OwnerAccount, "1234"), "4321"), Is.False);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(EnterUgcMapPacket.WrongPassword(OwnerAccount)) }));
    }

    [Test]
    public void APasswordHome_TakesTheRightPassword() {
        Assert.That(HomeEntry.TryEnter(session, HomeOf(OwnerAccount, "1234"), "1234"), Is.True);
        Assert.That(test.TakeSent(), Is.Empty);
    }
}

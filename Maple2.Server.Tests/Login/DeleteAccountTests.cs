using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Login;

// The account delete button of the character list: CheckDeleteAccount (0xBE, i64 account id) is answered with op
// 0x132 (i32 result, i64 character id), DeleteAccount (0xBF) with op 0x133 (i32 result). Unanswered, the button did
// nothing.
public class DeleteAccountTests {
    private TestLoginSession test = null!;
    private LoginSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        session = test.Start();
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private string Check() {
        new CheckDeleteAccountHandler { GameStorage = test.Storage }
            .Handle(session, TestGameSession.Packet(writer => writer.WriteLong(test.AccountId)));
        return Convert.ToHexString(test.TakeSent().Single());
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Check_NothingBlocks_AnswersZero() {
        Assert.That(Check(), Is.EqualTo("3201" + "00000000" + "0000000000000000"));
    }

    [Test]
    public void Check_UnreadMail_NamesTheCharacter() {
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.CreateMail(new Mail { ReceiverId = test.CharacterId }), Is.Not.Null);
        }

        Assert.That(Check(), Is.EqualTo(Hex(DeleteAccountPacket.Check(DeleteAccountRules.Mail, test.CharacterId))));
    }

    [Test]
    public void Check_PendingDelete_NamesTheCharacter() {
        long other = test.AddCharacter("Waiting");
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.UpdateDelete(test.AccountId, other, DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()), Is.True);
        }

        Assert.That(Check(), Is.EqualTo(Hex(DeleteAccountPacket.Check(DeleteAccountRules.CharacterPending, other))));
    }

    // Another account's blocks are not this account's.
    [Test]
    public void Check_OtherAccount_IsNotLooked_At() {
        long other = test.AddCharacter("Elsewhere", accountId: test.AccountId + 1);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.CreateMail(new Mail { ReceiverId = other }), Is.Not.Null);
        }

        Assert.That(Check(), Is.EqualTo(Hex(DeleteAccountPacket.Check(DeleteAccountRules.Ok))));
    }

    [Test]
    public void Delete_IsRefused() {
        new DeleteAccountHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(test.AccountId);
            writer.WriteUnicodeString("a");
            writer.WriteUnicodeString("b");
            writer.WriteUnicodeString("c");
        }));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { "3301" + "20000000" }));
    }

    [TestCase(true, false, false, false, false, false, false, DeleteAccountRules.CharacterPending)]
    [TestCase(false, true, true, false, false, false, false, DeleteAccountRules.BlackMarket)]
    [TestCase(false, false, true, false, true, false, false, DeleteAccountRules.Guild)]
    [TestCase(false, false, false, true, false, false, false, DeleteAccountRules.UgcMarket)]
    [TestCase(false, false, false, false, true, true, false, DeleteAccountRules.Wedding)]
    [TestCase(false, false, false, false, false, false, true, DeleteAccountRules.MesoMarket)]
    [TestCase(false, false, false, false, false, false, false, DeleteAccountRules.Ok)]
    public void Block(bool pending, bool blackMarket, bool guild, bool ugc, bool married, bool mail, bool meso, int expected) {
        Assert.That(DeleteAccountRules.Block(new DeleteAccountRules.Holdings(pending, blackMarket, guild, ugc, married, mail, meso)),
            Is.EqualTo(expected));
    }
}

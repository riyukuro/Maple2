using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Login;

// A create, delete, restore or rename that fails where no check answers (a database or world failure that throws)
// still gets its system error. The client's character list takes no input until the answer comes, and the exception
// used to end the connection with no answer.
public class CharacterManagementFailureTests {
    private TestLoginSession test = null!;
    private LoginSession session = null!;
    private BanWordStorage words = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        words = new BanWordStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        var tutorial = new JobTable.Tutorial(2000023, 0, 0, [], [], [], []);
        var skills = new Dictionary<SkillRank, JobTable.Skill[]> { [SkillRank.Basic] = [], [SkillRank.Awakening] = [] };
        test.Rows.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Knight] = new(tutorial, skills, []),
        }));
        session = test.Start();
        using (var context = new Ms2Context(test.Rows.Options)) {
            context.Account.Find(test.AccountId)!.MaxCharacters = 4;
            context.SaveChanges();
        }
        session.ListCharacters();
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    // brokenStorage / brokenWords: a storage or ban word list whose first use throws.
    private CharacterManagementHandler Handler(bool brokenStorage = false, bool brokenWords = false) {
        return new CharacterManagementHandler {
            World = test.World,
            GameStorage = brokenStorage ? null! : test.Storage,
            BanWordStorage = brokenWords ? null! : words,
            ItemMetadata = null!,
            TableMetadata = test.Rows.TableMetadata,
            ServerTableMetadata = test.Rows.ServerTableMetadata,
        };
    }

    private void Send(CharacterManagementHandler handler, byte command, long characterId, string? name = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteLong(characterId);
            if (name != null) {
                writer.WriteUnicodeString(name);
            }
        }));
    }

    private long PendingCharacter(DateTime deleteTime) {
        long characterId = test.AddCharacter("Gone");
        using var context = new Ms2Context(test.Rows.Options);
        context.Character.Find(characterId)!.DeleteTime = deleteTime;
        context.SaveChanges();
        return characterId;
    }

    private bool Stored(long characterId) {
        using var context = new Ms2Context(test.Rows.Options);
        return context.Character.Any(character => character.Id == characterId);
    }

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Create_AFailureGetsTheSystemError() {
        // The client's layout with no equips (CharacterCreateRequestTests).
        Handler(brokenWords: true).Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteByte(0);
            writer.WriteShort((short) JobCode.Knight);
            writer.WriteUnicodeString("Parry");
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteShort(1);
            writer.WriteByte(0);
            writer.WriteInt(0);
        }));

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CharacterListPacket.CreateError(CharacterCreateError.s_char_err_system)) }));
    }

    [Test]
    public void Rename_AFailureGetsTheSystemText() {
        long due = test.AddCharacter("Abcdefghijklm");

        Send(Handler(brokenWords: true), 5, due, "Parry");

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CharacterListPacket.NameRefused(NameChangeRules.Refusal.Invalid, due, "Parry")) }));
        Assert.That(test.Name(due), Is.EqualTo("Abcdefghijklm"));
    }

    // A character in its wait gets the wait's answer, which keeps the client's final delete panel; the delete
    // (mode 2 after the wait) and the final delete (mode 4) alike.
    [TestCase((byte) 2)]
    [TestCase((byte) 4)]
    public void Delete_AFailureInTheWaitKeepsTheCharacter(byte command) {
        long characterId = PendingCharacter(DateTime.UnixEpoch.AddSeconds(1));
        test.World.ClubThrows = new InvalidOperationException("world failure");

        Send(Handler(), command, characterId);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CharacterListPacket.BeginDelete(characterId, 1, CharacterDeleteError.s_char_err_destroy)) }));
        Assert.That(Stored(characterId), Is.True);
    }

    // The delete time cannot be read either: the delete answer with the error.
    [Test]
    public void Delete_AFailureWithNoStorageGetsTheDeleteError() {
        long characterId = test.AddCharacter("Kept");

        Send(Handler(brokenStorage: true), 2, characterId);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CharacterListPacket.DeleteEntry(characterId, CharacterDeleteError.s_char_err_destroy)) }));
        Assert.That(Stored(characterId), Is.True);
    }

    // A failure after the final delete went through (freeing the burning levelup pick, here a server whose events cannot be
    // read) is logged; the delete keeps its one answer, with no error box after it.
    [Test]
    public void Delete_AFailureAfterTheDeleteIsNotAnswered() {
        long characterId = PendingCharacter(DateTime.UnixEpoch.AddSeconds(1));
        typeof(Maple2.Server.Login.LoginServer).GetField("eventCache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(session.Server, null);

        Send(Handler(), 4, characterId);

        Assert.Multiple(() => {
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(CharacterListPacket.DeleteEntry(characterId)) }));
            Assert.That(Stored(characterId), Is.False);
        });
    }

    [Test]
    public void CancelDelete_AFailureGetsTheRestoreError() {
        long characterId = PendingCharacter(DateTime.UtcNow.AddDays(1));

        Send(Handler(brokenStorage: true), 3, characterId);

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(CharacterListPacket.CancelDelete(characterId, CharacterDeleteError.s_char_err_destroy)) }));
    }

    // A request the client could not have written (a create cut short after its name) is still dropped by the router
    // with no answer: the router's bounded reader throws past the catch.
    [Test]
    public void Create_ACutShortPacketIsNotAnswered() {
        var packet = TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteByte(0);
            writer.WriteShort((short) JobCode.Knight);
            writer.WriteUnicodeString("Parry");
        });
        Assert.Throws<Maple2.Server.Core.Network.PacketOverreadException>(() =>
            Handler().Handle(session, new Maple2.Server.Core.Network.BoundedPacketReader(packet, packet.Available)));
        Assert.That(Sent(), Is.Empty);
    }
}

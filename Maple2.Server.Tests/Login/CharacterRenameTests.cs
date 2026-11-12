using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Login;

// The forced rename of the login scene (CharacterManagement 5) for a character whose name is too long: the end
// flag of the character list, the answer to a select, and the rename itself.
public class CharacterRenameTests {
    private const string LONG_NAME = "Abcdefghijklm"; // 13
    private TestLoginSession test = null!;
    private CharacterManagementHandler handler = null!;
    private LoginSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        words.BanWordMetadata.Add(new BanWordMetadata(0, "badword", false));
        words.SaveChanges();
        handler = new CharacterManagementHandler {
            World = test.World, GameStorage = test.Storage, BanWordStorage = new BanWordStorage(words),
            ItemMetadata = null!, TableMetadata = test.Rows.TableMetadata, ServerTableMetadata = test.Rows.ServerTableMetadata,
        };
        session = test.Start();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private void Select(long characterId) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(characterId);
            writer.WriteShort(1);
        }));
    }

    private void Rename(long characterId, string name) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteLong(characterId);
            writer.WriteUnicodeString(name);
        }));
    }

    private static string RefusalBox(NameChangeRules.Refusal refusal, string name) {
        (StringCode code, string[] args) = NameChangeRules.Text(refusal, name);
        return Convert.ToHexString(NoticePacket.MessageBox(new InterfaceText(code, args)).ToArray());
    }

    [Test]
    public void Packets() {
        Assert.Multiple(() => {
            // LoginToGame: code 49, an empty text, the character.
            Assert.That(Convert.ToHexString(MigrationPacket.LoginToGameRename(0x0102030405060708).ToArray()),
                Is.EqualTo("0E00" + "31" + "0000" + "0807060504030201"));
            // CharacterList 4 with and without the flag.
            Assert.That(Convert.ToHexString(CharacterListPacket.EndList(true).ToArray()), Is.EqualTo("0C00" + "04" + "01"));
            Assert.That(Convert.ToHexString(CharacterListPacket.EndList().ToArray()), Is.EqualTo("0C00" + "04" + "00"));
            // CharacterList 7 as KMS2 sends it after the name change item.
            Assert.That(Convert.ToHexString(CharacterListPacket.NameChanged(0x0102030405060708, "Parry").ToArray()),
                Is.EqualTo("0C00" + "07" + "01000000" + "0807060504030201" + "0500" + "50006100720072007900"));
        });
    }

    // The count is the client's of the server's region (ScanLimit): one per character, and one more for a
    // character the region counts twice. KR counts Hangul twice; NA counts no character twice (its client has
    // no Hangul case), so there only a name of more than 12 characters is due; CN counts the ideographs twice.
    [TestCase("Abcdefghijkl", false, false, false)]
    [TestCase(LONG_NAME, true, true, true)]
    [TestCase("\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00", false, false, false)] // 6 Hangul count 12 in KR
    [TestCase("\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00a", true, false, false)]
    [TestCase("\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\u00E9", true, false, false)] // a character the region does not allow counts once
    [TestCase("\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00\uAC00", true, true, true)]
    [TestCase("\u4E00\u4E00\u4E00\u4E00\u4E00\u4E00a", false, false, true)]
    [TestCase("Abcdefghijk\u00E9", false, false, false)]
    public void RenameIsDueForANameThatIsTooLong(string name, bool kr, bool na, bool cn) {
        Assert.Multiple(() => {
            Assert.That(NameChangeRules.IsRenameDue(NameValidator.CharacterNameScanWidth(name, "KR"), 12), Is.EqualTo(kr), "KR");
            Assert.That(NameChangeRules.IsRenameDue(NameValidator.CharacterNameScanWidth(name, "NA"), 12), Is.EqualTo(na), "NA");
            Assert.That(NameChangeRules.IsRenameDue(NameValidator.CharacterNameScanWidth(name, "CN"), 12), Is.EqualTo(cn), "CN");
            // The session's own test is the one of the region the server runs for.
            bool due = Target.LOCALE switch { "NA" => na, "CN" => cn, _ => kr };
            Assert.That(LoginSession.IsRenameDue(name), Is.EqualTo(due), Target.LOCALE);
        });
    }

    // The list's end flag is set only when a character of the account is due.
    [Test]
    public void TheListEndsWithTheFlagOnlyWhenACharacterIsDue() {
        session.ListCharacters();
        List<byte[]> sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo("0C000400"));

        test.AddCharacter(LONG_NAME);
        session.ListCharacters();
        sent = test.TakeSent();
        Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo("0C000401"));
        Assert.That(test.Errors, Is.Empty);
    }

    // A character that is due does not enter: LoginToGame 49 with the character, and no move.
    [Test]
    public void SelectOfADueCharacterAsksForTheRename() {
        long due = test.AddCharacter(LONG_NAME);

        Select(due);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Convert.ToHexString(MigrationPacket.LoginToGameRename(due).ToArray()),
        }));
        Assert.That(test.World.Moves, Is.Empty);
    }

    [Test]
    public void SelectOfAnotherCharacterEnters() {
        Select(test.CharacterId);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.LoginToGame }));
        Assert.That(test.World.Moves.Select(move => move.CharacterId), Is.EqualTo(new[] { test.CharacterId }));
    }

    // An accepted name: stored, told to the world, and the list is sent again without the flag.
    [Test]
    public void RenameStoresTheNameAndListsAgain() {
        long due = test.AddCharacter(LONG_NAME);

        Rename(due, "Parry");

        List<byte[]> sent = test.TakeSent();
        Assert.Multiple(() => {
            Assert.That(test.Name(due), Is.EqualTo("Parry"));
            Assert.That(test.World.PlayerUpdates.Select(update => (update.AccountId, update.CharacterId, update.HasName, update.Name)),
                Is.EqualTo(new[] { (test.AccountId, due, true, "Parry") }));
            Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] {
                SendOp.CharMaxCount, SendOp.CharacterList, SendOp.CharacterList, SendOp.CharacterList,
            }));
            Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo("0C000400"));
            Assert.That(test.Errors, Is.Empty);
        });

        // The character now enters.
        Select(due);
        Assert.That(test.World.Moves.Select(move => move.CharacterId), Is.EqualTo(new[] { due }));
    }

    // Each refused name: the client's own text in a message box, the name unchanged, the world not told.
    [TestCase("", NameChangeRules.Refusal.Invalid)]
    [TestCase("   ", NameChangeRules.Refusal.Invalid)]
    [TestCase("A", NameChangeRules.Refusal.Invalid)]
    [TestCase("Abcdefghijklmn", NameChangeRules.Refusal.Invalid)]
    [TestCase("Par_ry", NameChangeRules.Refusal.Invalid)]
    [TestCase("xBadWordx", NameChangeRules.Refusal.Banned)]
    [TestCase("Taken", NameChangeRules.Refusal.Taken)]
    [TestCase("taKEN", NameChangeRules.Refusal.Taken)]
    public void ARefusedNameChangesNothing(string name, NameChangeRules.Refusal refusal) {
        long due = test.AddCharacter(LONG_NAME);
        test.AddCharacter("Taken", accountId: 2000);

        Rename(due, name);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { RefusalBox(refusal, name) }));
            Assert.That(test.Name(due), Is.EqualTo(LONG_NAME));
            Assert.That(test.World.PlayerUpdates, Is.Empty);
        });
        // Still due.
        Select(due);
        Assert.That(test.World.Moves, Is.Empty);
    }

    // A character that is not due, or not the account's: the move error, and nothing changes.
    [Test]
    public void OnlyADueCharacterOfTheAccountIsRenamed() {
        long others = test.AddCharacter(LONG_NAME, accountId: 2000);
        string own = test.Name(test.CharacterId);

        Rename(test.CharacterId, "Parry");
        Rename(others, "Parry");
        Rename(987654321, "Parry");

        string error = Convert.ToHexString(MigrationPacket.LoginToGameError(Maple2.Model.Error.MigrationError.s_move_err_default, "Invalid character").ToArray());
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { error, error, error }));
            Assert.That(test.Name(test.CharacterId), Is.EqualTo(own));
            Assert.That(test.Name(others), Is.EqualTo(LONG_NAME));
            Assert.That(test.World.PlayerUpdates, Is.Empty);
        });
    }

    // A create that takes the name first: the rename is refused. A rename that takes it first: the create's name
    // check (GetCharacterId) finds it.
    [Test]
    public void ARenameAndACreateWithTheSameName() {
        long first = test.AddCharacter(LONG_NAME);
        long second = test.AddCharacter(LONG_NAME + "n");

        test.AddCharacter("Parry", accountId: 2000); // the create
        Rename(first, "Parry");
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { RefusalBox(NameChangeRules.Refusal.Taken, "Parry") }));
        Assert.That(test.Name(first), Is.EqualTo(LONG_NAME));

        Rename(second, "Garry");
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetCharacterId("garry"), Is.EqualTo(second));
    }

    // The write itself refuses a name that another character took after the handler's check, and keeps the old name.
    [Test]
    public void TheWriteRefusesANameTakenSinceTheCheck() {
        long due = test.AddCharacter(LONG_NAME);
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetCharacterId("Parry"), Is.EqualTo(0));
        test.AddCharacter("PARRY", accountId: 2000);

        Assert.Multiple(() => {
            Assert.That(db.RenameCharacter(test.AccountId, due, "Parry"), Is.EqualTo(NameChangeRules.Refusal.Taken));
            Assert.That(test.Name(due), Is.EqualTo(LONG_NAME));
            // Another account's character is not renamed; the own name in another case is.
            Assert.That(db.RenameCharacter(2000, due, "Free"), Is.EqualTo(NameChangeRules.Refusal.Invalid));
            Assert.That(db.RenameCharacter(test.AccountId, due, "Free"), Is.EqualTo(NameChangeRules.Refusal.None));
            Assert.That(db.RenameCharacter(test.AccountId, due, "FREE"), Is.EqualTo(NameChangeRules.Refusal.None));
            Assert.That(test.Name(due), Is.EqualTo("FREE"));
        });
    }
}

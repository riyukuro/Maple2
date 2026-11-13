using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using static Maple2.Model.Error.CharacterDeleteError;

namespace Maple2.Server.Tests.Login;

// The restore of a character whose delete is pending (CharacterManagement 3). It only ends the wait; the blocks of a
// delete do not hold it back. Every answer is CharacterList 6, the client's restore answer (UICharacterListDialog
// mode 6: 0 clears the timer, another code shows its delete text and leaves the timer).
public class CharacterRestoreTests {
    private TestLoginSession test = null!;
    private CharacterManagementHandler handler = null!;
    private LoginSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
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

    private void Restore(long characterId) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(characterId);
        }));
    }

    private void SetPending(long characterId) {
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.UpdateDelete(test.AccountId, characterId, DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()), Is.True);
    }

    private long DeleteTime(long characterId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetCharacter(characterId, test.AccountId)!.DeleteTime;
    }

    private static string Answer(long characterId, Maple2.Model.Error.CharacterDeleteError error = ok) {
        return Convert.ToHexString(CharacterListPacket.CancelDelete(characterId, error).ToArray());
    }

    [Test]
    public void APendingDeleteIsRestored() {
        SetPending(test.CharacterId);

        Restore(test.CharacterId);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Answer(test.CharacterId) }));
            Assert.That(DeleteTime(test.CharacterId), Is.EqualTo(0));
        });
    }

    // Unread mail blocks a delete (error 7) but not the restore: the restore used to answer with the delete's
    // DeleteEntry 7, which the client takes as a restore, while the row kept its delete time.
    [Test]
    public void UnreadMailDoesNotHoldBackTheRestore() {
        SetPending(test.CharacterId);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.CreateMail(new Mail { ReceiverId = test.CharacterId }), Is.Not.Null);
        }

        Restore(test.CharacterId);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Answer(test.CharacterId) }));
            Assert.That(DeleteTime(test.CharacterId), Is.EqualTo(0));
        });
    }

    [Test]
    public void ACharacterWithNoPendingDeleteGetsError8() {
        Restore(test.CharacterId);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Answer(test.CharacterId, s_char_err_no_destroy_wait) }));
    }

    // Another account's character, or none: error 1, and the other account's pending delete stays.
    [Test]
    public void AnotherAccountsCharacterGetsError1() {
        long others = test.AddCharacter("Others", accountId: 2000);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.UpdateDelete(2000, others, DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()), Is.True);
        }

        Restore(others);
        Restore(987654321);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Answer(others, s_char_err_already_destroy), Answer(987654321, s_char_err_already_destroy),
            }));
            using GameStorage.Request db = test.Storage.Context();
            Assert.That(db.GetCharacter(others, 2000)!.DeleteTime, Is.GreaterThan(0));
        });
    }
}

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
using static Maple2.Model.Error.MigrationError;

namespace Maple2.Server.Tests.Login;

// The character list's own checks around a delete wait. The client never starts a character whose delete time is
// set (UICharacterListDialog OnGameStart returns early), and sends the final delete (CharacterManagement 4) only
// from the final delete button of a character in wait (onClickFinalDelete).
public class CharacterDeleteWaitTests {
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
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private void Send(byte mode, long characterId) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            writer.WriteLong(characterId);
            if (mode == 0) {
                writer.WriteShort(0);
            }
        }));
    }

    private void SetPending(long characterId) {
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.UpdateDelete(test.AccountId, characterId, DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()), Is.True);
    }

    private (bool Stored, long DeleteTime) Row(long characterId) {
        using GameStorage.Request db = test.Storage.Context();
        var character = db.GetCharacter(characterId, test.AccountId);
        return (character != null, character?.DeleteTime ?? 0);
    }

    // A character in delete wait does not enter the game: the select used to migrate it out.
    [Test]
    public void ACharacterInDeleteWaitIsNotSelected() {
        SetPending(test.CharacterId);

        Send(0, test.CharacterId);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Convert.ToHexString(MigrationPacket.LoginToGameError(s_move_err_default, "Character is waiting for deletion").ToArray()),
            }));
            Assert.That(test.World.Moves, Is.Empty);
        });
    }

    // A final delete of a character with no wait: error 8, in the delete answer (CharacterList 2) that lets the
    // list take input again. It used to start a wait, or delete a character below the wait level outright.
    [Test]
    public void AFinalDeleteWithNoWaitGetsError8() {
        Send(4, test.CharacterId);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Convert.ToHexString(CharacterListPacket.DeleteEntry(test.CharacterId, s_char_err_no_destroy_wait).ToArray()),
            }));
            Assert.That(Row(test.CharacterId), Is.EqualTo((true, 0L)));
        });
    }

    // The delete's own blocks answer first (unread mail: error 7), as for the delete button; error 8 only when
    // nothing blocks the delete.
    [Test]
    public void AFinalDeleteWithNoWaitGetsTheDeleteBlockFirst() {
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.CreateMail(new Mail { ReceiverId = test.CharacterId }), Is.Not.Null);
        }

        Send(4, test.CharacterId);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Convert.ToHexString(CharacterListPacket.DeleteEntry(test.CharacterId, s_char_err_unread_mail).ToArray()),
            }));
            Assert.That(Row(test.CharacterId), Is.EqualTo((true, 0L)));
        });
    }

    // The final delete of a character whose wait has passed deletes it.
    [Test]
    public void AFinalDeleteOfADueWaitDeletes() {
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.UpdateDelete(test.AccountId, test.CharacterId, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()), Is.True);
        }

        Send(4, test.CharacterId);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Convert.ToHexString(CharacterListPacket.DeleteEntry(test.CharacterId).ToArray()),
            }));
            Assert.That(Row(test.CharacterId).Stored, Is.False);
        });
    }

    // The final delete of a character still in wait: its delete time with error 10, the character kept.
    [Test]
    public void AFinalDeleteBeforeTheWaitEndsKeepsTheCharacter() {
        SetPending(test.CharacterId);
        long deleteTime = Row(test.CharacterId).DeleteTime;

        Send(4, test.CharacterId);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Convert.ToHexString(CharacterListPacket.BeginDelete(test.CharacterId, deleteTime, s_char_err_next_delete_char_date).ToArray()),
            }));
            Assert.That(Row(test.CharacterId), Is.EqualTo((true, deleteTime)));
        });
    }

    // The delete button (CharacterManagement 2) of a character with no wait still starts it or deletes.
    [Test]
    public void ADeleteWithNoWaitIsStillTaken() {
        Send(2, test.CharacterId);

        (bool stored, long deleteTime) = Row(test.CharacterId);
        Assert.That(!stored || deleteTime > 0, Is.True);
    }
}

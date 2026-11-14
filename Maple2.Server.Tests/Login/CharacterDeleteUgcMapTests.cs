using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Model;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Login;

// An account that owns an outdoor plot keeps at least one active character (no delete pending): the delete of its
// last one is refused with s_char_err_exist_ugc_map (2), which the client shows from the delete reply
// (UICharacterListDialog mode 5). A character whose delete is pending is not active: completing its delete is not
// refused, and a plot past its expiry is not owned.
public class CharacterDeleteUgcMapTests {
    private TestLoginSession test = null!;
    private CharacterManagementHandler handler = null!;
    private LoginSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        handler = new CharacterManagementHandler {
            World = test.World,
            GameStorage = test.Storage,
            BanWordStorage = new BanWordStorage(words),
            ItemMetadata = null!,
            TableMetadata = test.Rows.TableMetadata,
            ServerTableMetadata = test.Rows.ServerTableMetadata,
        };
        session = test.Start();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private void AddPlot(bool indoor, bool expired = false) {
        using var context = new Ms2Context(test.Rows.Options);
        context.UgcMap.Add(new UgcMap {
            OwnerId = test.AccountId, MapId = indoor ? 62000000 : 2000062, Indoor = indoor, Number = 1,
            ExpiryTime = DateTimeOffset.UtcNow.AddDays(expired ? -1 : 7),
        });
        context.SaveChanges();
    }

    private void SetDeleteTime(long characterId, long seconds) {
        using var context = new Ms2Context(test.Rows.Options);
        context.Character.Find(characterId)!.DeleteTime = DateTime.UnixEpoch.AddSeconds(seconds);
        context.SaveChanges();
    }

    // The account's only other character has its delete pending: this one is the last active one.
    private long LastActiveCharacter() {
        long characterId = test.AddCharacter("Last");
        SetDeleteTime(test.CharacterId, 1);
        return characterId;
    }

    // Delete (2) begins a delete, or ends a pending one; ConfirmDelete (4) is the client's completion of a pending one.
    private void Delete(long characterId, byte command = 2) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteLong(characterId);
        }));
    }

    private bool Stored(long characterId) {
        using var context = new Ms2Context(test.Rows.Options);
        return context.Character.Any(character => character.Id == characterId);
    }

    // The delete of an active character went on: it began its wait (the test constants put every level over the
    // division level), with no s_char_err_exist_ugc_map reply.
    private void AssertDeleteBegun(long characterId) {
        string refused = Convert.ToHexString(CharacterListPacket.DeleteEntry(characterId, CharacterDeleteError.s_char_err_exist_ugc_map).ToArray());
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Has.None.EqualTo(refused));
        using var context = new Ms2Context(test.Rows.Options);
        Assert.That(context.Character.Find(characterId)!.DeleteTime, Is.Not.EqualTo(default(DateTime)));
    }

    [Test]
    public void AnOutdoorPlotKeepsTheLastActiveCharacter() {
        long characterId = LastActiveCharacter();
        AddPlot(indoor: false);
        test.TakeSent();

        Delete(characterId);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Convert.ToHexString(CharacterListPacket.DeleteEntry(characterId, CharacterDeleteError.s_char_err_exist_ugc_map).ToArray()),
        }));
        Assert.That(Stored(characterId), Is.True);
    }

    // Another character with no delete pending stays active: the delete goes on.
    [Test]
    public void AnotherActiveCharacterLetsTheDeleteGoOn() {
        long characterId = test.AddCharacter("Gone");
        SetDeleteTime(characterId, 1);
        AddPlot(indoor: false);
        test.TakeSent();

        Delete(characterId);

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(CharacterListPacket.DeleteEntry(characterId).ToArray()) }));
        Assert.That(Stored(characterId), Is.False);
    }

    // Every other character is pending too, but this one's delete is pending already: completing it leaves the
    // active count where it was (none), so it goes on.
    [Test]
    public void CompletingAPendingDeleteIsNotRefused() {
        long characterId = test.AddCharacter("Gone");
        SetDeleteTime(characterId, 1);
        SetDeleteTime(test.CharacterId, 1);
        AddPlot(indoor: false);
        test.TakeSent();

        Delete(characterId, command: 4);

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(CharacterListPacket.DeleteEntry(characterId).ToArray()) }));
        Assert.That(Stored(characterId), Is.False);
    }

    // A plot past its expiry that World has not taken back yet is no longer owned.
    [Test]
    public void AnExpiredPlotDoesNotBlock() {
        long characterId = LastActiveCharacter();
        AddPlot(indoor: false, expired: true);
        test.TakeSent();

        Delete(characterId);

        AssertDeleteBegun(characterId);
    }

    // The home (an indoor ugc map) is not real estate.
    [Test]
    public void TheHomeAloneDoesNotBlock() {
        long characterId = LastActiveCharacter();
        AddPlot(indoor: true);
        test.TakeSent();

        Delete(characterId);

        AssertDeleteBegun(characterId);
    }
}

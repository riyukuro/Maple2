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
// (UICharacterListDialog mode 5).
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

    private void AddPlot(bool indoor) {
        using var context = new Ms2Context(test.Rows.Options);
        context.UgcMap.Add(new UgcMap { OwnerId = test.AccountId, MapId = indoor ? 62000000 : 2000062, Indoor = indoor, Number = 1 });
        context.SaveChanges();
    }

    private void SetDeleteTime(long characterId, long seconds) {
        using var context = new Ms2Context(test.Rows.Options);
        context.Character.Find(characterId)!.DeleteTime = DateTime.UnixEpoch.AddSeconds(seconds);
        context.SaveChanges();
    }

    // The only other character of the account has its delete pending too.
    private long LastActiveCharacter() {
        long characterId = test.AddCharacter("Gone");
        SetDeleteTime(characterId, 1);
        SetDeleteTime(test.CharacterId, 1);
        return characterId;
    }

    private void Delete(long characterId) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(characterId);
        }));
    }

    private bool Stored(long characterId) {
        using var context = new Ms2Context(test.Rows.Options);
        return context.Character.Any(character => character.Id == characterId);
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

    // The home (an indoor ugc map) is not real estate.
    [Test]
    public void TheHomeAloneDoesNotBlock() {
        long characterId = LastActiveCharacter();
        AddPlot(indoor: true);
        test.TakeSent();

        Delete(characterId);

        Assert.That(Stored(characterId), Is.False);
    }
}

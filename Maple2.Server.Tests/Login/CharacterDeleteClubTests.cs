using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Login;

// A character deletion first has the world take the character out of its clubs (the other members get Club 0x1c
// and a leader hands the club on), since the club rows name the character. A world that cannot do it keeps the
// character.
public class CharacterDeleteClubTests {
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

    // A character whose delete wait has passed: the next delete request removes it.
    private long PendingCharacter() {
        long characterId = test.AddCharacter("Gone");
        using var context = new Ms2Context(test.Rows.Options);
        context.Character.Find(characterId)!.DeleteTime = DateTime.UnixEpoch.AddSeconds(1);
        context.SaveChanges();
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
    public void Delete_TheWorldTakesTheCharacterOutOfItsClubsFirst() {
        long characterId = PendingCharacter();
        test.TakeSent();

        Delete(characterId);

        ClubRequest request = test.World.Clubs.Single();
        Assert.That(request.ClubCase, Is.EqualTo(ClubRequest.ClubOneofCase.DeleteCharacter));
        Assert.That(request.RequestorId, Is.EqualTo(characterId));
        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(CharacterListPacket.DeleteEntry(characterId).ToArray()) }));
        Assert.That(Stored(characterId), Is.False);
    }

    [Test]
    public void Delete_AWorldRefusalKeepsTheCharacter() {
        long characterId = PendingCharacter();
        test.World.ClubError = (int) ClubError.s_club_err_unknown;
        test.TakeSent();

        Delete(characterId);

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(CharacterListPacket.DeleteEntry(characterId, CharacterDeleteError.s_char_err_destroy).ToArray()) }));
        Assert.That(Stored(characterId), Is.True);
    }
}

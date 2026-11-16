using System;
using System.Linq;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;


namespace Maple2.Server.Tests.Login;

// A delete while the account is in game: another client of it entered after this login was let in. As a select
// does, the delete ends that game session first (a forced Disconnect, whose end saves it), and its checks then read
// what the session stored. Before, the character was deleted under its live session: a guild it came to lead or a
// marriage it made in game was left without it.
public class CharacterDeleteOnlineTests {
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
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private string[] Delete(long characterId) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(characterId);
        }));
        return test.TakeSent().Select(Convert.ToHexString).ToArray();
    }

    private bool Stored(long characterId) {
        using var context = new Ms2Context(test.Rows.Options);
        return context.Character.Any(character => character.Id == characterId);
    }

    private static string Entry(long characterId, CharacterDeleteError error = CharacterDeleteError.ok) =>
        Convert.ToHexString(CharacterListPacket.DeleteEntry(characterId, error).ToArray());

    // The delete went on: the character's wait began (CharacterList 5 with no error; the test characters are at the
    // division level, so a delete waits) and its delete time is stored.
    private void AssertWaitBegan(string[] sent, long characterId) {
        Assert.That(sent, Has.Length.EqualTo(1));
        Assert.That(sent[0], Does.StartWith("0C0005" + Convert.ToHexString(BitConverter.GetBytes(characterId)) + "00000000"));
        using var context = new Ms2Context(test.Rows.Options);
        Assert.That(context.Character.Single(character => character.Id == characterId).DeleteTime, Is.Not.Zero);
    }

    private void InGame(long characterId) {
        test.World.Account = new PlayerInfoResponse { AccountId = test.AccountId, CharacterId = characterId, Channel = 2 };
    }

    // The live session became the leader of a guild; its end stores that, and the delete is refused for it.
    [Test]
    public void TheSessionsLastSave_IsCheckedBeforeTheDelete() {
        long characterId = test.AddCharacter("Live");
        InGame(characterId);
        test.World.OnDisconnect = _ => {
            using GameStorage.Request db = test.Storage.Context();
            Assert.That(db.CreateGuild("Made", characterId), Is.Not.Null);
        };

        string[] sent = Delete(characterId);

        Assert.Multiple(() => {
            Assert.That(test.World.Disconnects.Select(request => (request.CharacterId, request.Force)), Is.EqualTo(new[] { (characterId, true) }));
            Assert.That(sent, Is.EqualTo(new[] { Entry(characterId, CharacterDeleteError.s_char_err_guild_master) }));
            Assert.That(Stored(characterId), Is.True);
        });
    }

    // Nothing to refuse: the session is ended, then the delete goes on.
    [Test]
    public void TheSessionIsEnded_ThenTheDeleteGoesOn() {
        long characterId = test.AddCharacter("Live");
        InGame(characterId);

        string[] sent = Delete(characterId);

        Assert.That(test.World.Disconnects, Has.Count.EqualTo(1));
        AssertWaitBegan(sent, characterId);
    }

    [Test]
    public void AnAccountNotInGame_EndsNothing() {
        long characterId = test.AddCharacter("Offline");

        AssertWaitBegan(Delete(characterId), characterId);
        Assert.That(test.World.Disconnects, Is.Empty);
    }

    // The session could not be ended (the world not reached): the delete is refused and the character kept.
    [Test]
    public void ASessionNotEnded_RefusesTheDelete() {
        long characterId = test.AddCharacter("Live");
        InGame(characterId);
        test.World.DisconnectError = new RpcException(new Status(StatusCode.Unavailable, "down"));

        Assert.That(Delete(characterId), Is.EqualTo(new[] { Entry(characterId, CharacterDeleteError.s_char_err_destroy) }));
        Assert.That(Stored(characterId), Is.True);
    }

    // A session that went offline in between (NotFound): nothing to end, the delete goes on.
    [Test]
    public void ARecordThatWentOfflineMeanwhile_StillDeletes() {
        long characterId = test.AddCharacter("Live");
        InGame(characterId);
        test.World.DisconnectError = new RpcException(new Status(StatusCode.NotFound, "offline"));

        AssertWaitBegan(Delete(characterId), characterId);
    }
}

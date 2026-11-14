using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Login;

// A character in a guild is not deleted: its guild leader gets s_char_err_guild_master, a member s_char_err_guild.
// The guild is not a character column (GuildMember holds it), so the check reads the member row.
public class CharacterDeleteGuildTests {
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

    private static string Entry(long characterId, CharacterDeleteError error) =>
        Convert.ToHexString(CharacterListPacket.DeleteEntry(characterId, error).ToArray());

    [Test]
    public void Delete_GuildLeader_IsRefused() {
        long characterId = test.AddCharacter("Leader");
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.CreateGuild("Kept", characterId), Is.Not.Null);
        }

        Assert.That(Delete(characterId), Is.EqualTo(new[] { Entry(characterId, CharacterDeleteError.s_char_err_guild_master) }));
        Assert.That(Stored(characterId), Is.True);
    }

    [Test]
    public void Delete_GuildMember_IsRefused() {
        long leader = test.AddCharacter("Leader", accountId: test.AccountId + 1);
        long characterId = test.AddCharacter("Member");
        using (GameStorage.Request db = test.Storage.Context()) {
            Guild? guild = db.CreateGuild("Kept", leader);
            Assert.That(guild, Is.Not.Null);
            PlayerInfo? info = db.GetPlayerInfo(characterId);
            Assert.That(info, Is.Not.Null);
            Assert.That(db.CreateGuildMember(guild!.Id, info!), Is.Not.Null);
        }

        Assert.That(Delete(characterId), Is.EqualTo(new[] { Entry(characterId, CharacterDeleteError.s_char_err_guild) }));
        Assert.That(Stored(characterId), Is.True);
    }
}

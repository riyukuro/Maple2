using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// The wedding profile picture columns (migration AddWeddingProfiles) on MySQL: Ugc 12 of a married partner stores
// the path in that partner's column only, the web server's married test reads the row, and each viewer gets its own
// picture as the second partner. Runs only when MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game
// database.
public class WeddingProfileMySqlTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const string File = "0f8fad5b-d9cb-469f-a165-70867728950e";

    private TestGameSession test = null!;
    private GameSession session = null!;
    private DbContextOptions plain = null!;
    private long partnerId;
    private long marriageId;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the marriage columns need a real database: set MS2_TEST_GAME_DB");
        }
        ServerVersion version = ServerVersion.AutoDetect(Connection);
        plain = new DbContextOptionsBuilder().UseMySql(Connection, version).Options;
        test = new TestGameSession(new DbContextOptionsBuilder().UseMySql(Connection, version).Options);
        session = test.Start();
        using GameStorage.Request db = test.Storage.Context();
        Account account = db.CreateAccount(new Account { Username = "wp" + Guid.NewGuid().ToString("N")[..12] }, "pw");
        var partner = new Character { AccountId = account.Id, Name = "wp" + Guid.NewGuid().ToString("N")[..8], Mastery = new Mastery() };
        partner.ReturnMaps.Push(2000062);
        partnerId = db.CreateCharacter(partner)!.Id;
        marriageId = db.CreateMarriage(test.CharacterId, partnerId)!.Id;
    }

    [TearDown]
    public void TearDown() {
        session?.Dispose();
        if (Connection == null) {
            return;
        }
        using var context = new Ms2Context(plain);
        context.Marriage.Where(row => row.Id == marriageId).ExecuteDelete();
    }

    [Test]
    public void Ugc12_StoresThePartnersOwnColumn() {
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.IsMarried(test.CharacterId), Is.False);
            Assert.That(db.SetMarried(marriageId, DateTime.Now), Is.True);
            Assert.That(db.IsMarried(test.CharacterId), Is.True);
            Assert.That(db.IsMarried(partnerId), Is.True);
            session.Marriage.Marriage = db.GetMarriage(test.CharacterId)!;
        }
        string path = $"data/profiles/wedding/{test.CharacterId}/{File}.png";

        new UgcHandler { WebStorage = null!, ItemMetadata = null!, TableMetadata = null!, World = null! }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(12);
                writer.WriteUnicodeString(path);
            }));

        using (var context = new Ms2Context(plain)) {
            Maple2.Database.Model.Marriage row = context.Marriage.Single(row => row.Id == marriageId);
            bool selfFirst = row.Partner1Id == test.CharacterId;
            Assert.That(selfFirst ? row.Partner1Profile : row.Partner2Profile, Is.EqualTo(path));
            Assert.That(selfFirst ? row.Partner2Profile : row.Partner1Profile, Is.Empty);
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetMarriage(test.CharacterId)!.Partner2.Profile, Is.EqualTo(path));
            Assert.That(db.GetMarriage(partnerId)!.Partner1.Profile, Is.EqualTo(path));
        }
    }
}

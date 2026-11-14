using System;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Server.Core.Modules;
using Maple2.Server.Global.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.World;

// A ban's times are stored in UTC (CreateBan, ActiveBansQuery). MySQL gives them back with no kind, so the login's
// restricted answer read them as local times: the client's restriction box (LoginResult 7) showed the start and the
// end off by the server's UTC offset. The in-memory store keeps the kind: the tests need MS2_TEST_GAME_DB.
public class GlobalLoginBanTimeTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private DbContextOptions options = null!;
    private GameStorage storage = null!;
    private GlobalService service = null!;
    private long banId;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the stored kind is MySQL's: set MS2_TEST_GAME_DB");
        }
        // On a UTC host the old and the new times are the same: only a host off UTC tells them apart.
        Assume.That(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow), Is.Not.EqualTo(TimeSpan.Zero), "the host is on UTC");
        options = GameDbModule.Options(Connection);
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        service = new GlobalService(storage);
    }

    [TearDown]
    public void TearDown() {
        if (Connection == null) {
            return;
        }
        using var context = new Ms2Context(options);
        context.Database.ExecuteSqlRaw("DELETE FROM `ban` WHERE `Id` = {0}", banId);
    }

    private LoginResponse Login(string name, Guid machineId) => service.Login(new LoginRequest {
        Username = name,
        Password = "secret",
        MachineId = machineId.ToString(),
        ClientIp = "127.0.0.1",
    }, null!).Result;

    private static void AreNow(LoginResponse response, long before, long after) {
        Assert.That(response.Code, Is.EqualTo(LoginResponse.Types.Code.Restricted));
        Assert.That(response.BanStart, Is.InRange(before - 2, after + 2));
        Assert.That(response.BanExpiry, Is.InRange(before + 86400 - 2, after + 86400 + 2));
    }

    [Test]
    public void AccountBan_SendsItsTimes() {
        string name = "ban" + Guid.NewGuid().ToString("N")[..10];
        Guid machineId = Guid.NewGuid();
        LoginResponse first = Login(name, machineId);
        Assert.That(first.Code, Is.EqualTo(LoginResponse.Types.Code.Ok));
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (GameStorage.Request db = storage.Context()) {
            banId = db.CreateBan(first.AccountId, "test", TimeSpan.FromDays(1));
        }

        AreNow(Login(name, machineId), before, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    // The hardware ban is checked before the account: another name on the banned account's machine.
    [Test]
    public void MachineBan_SendsItsTimes() {
        Guid machineId = Guid.NewGuid();
        LoginResponse first = Login("ban" + Guid.NewGuid().ToString("N")[..10], machineId);
        Assert.That(first.Code, Is.EqualTo(LoginResponse.Types.Code.Ok));
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (GameStorage.Request db = storage.Context()) {
            banId = db.CreateBan(first.AccountId, "test", TimeSpan.FromDays(1), machineId: machineId);
        }

        AreNow(Login("ban" + Guid.NewGuid().ToString("N")[..10], machineId), before, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
}

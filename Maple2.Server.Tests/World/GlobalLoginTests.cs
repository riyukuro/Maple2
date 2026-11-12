using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maple2.Database.Storage;
using Maple2.Server.Global.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.World;

// GlobalService.Login with auto register, on an in-memory database: the client may open two login connections at
// once for a new name, and both must get the one account it makes.
public class GlobalLoginTests {
    private GameStorage storage = null!;
    private GlobalService service = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        service = new GlobalService(storage);
    }

    private LoginResponse[] LoginTogether(params string[] passwords) {
        var machineId = Guid.NewGuid().ToString();
        using var barrier = new Barrier(passwords.Length);
        Task<LoginResponse>[] logins = passwords.Select(password => Task.Run(() => {
            barrier.SignalAndWait();
            return service.Login(new LoginRequest {
                Username = "NewName",
                Password = password,
                MachineId = machineId,
                ClientIp = "127.0.0.1",
            }, null!).Result;
        })).ToArray();
        return Task.WhenAll(logins).Result;
    }

    private long AccountId() {
        using GameStorage.Request db = storage.Context();
        return db.GetAccount("newname")?.Id ?? 0;
    }

    [Test]
    public void TwoLoginsForANewName_GetOneAccount() {
        LoginResponse[] responses = LoginTogether("secret", "secret");

        Assert.That(responses.Select(response => response.Code), Is.All.EqualTo(LoginResponse.Types.Code.Ok));
        Assert.That(responses[0].AccountId, Is.Not.Zero);
        Assert.That(responses[1].AccountId, Is.EqualTo(responses[0].AccountId));
        Assert.That(AccountId(), Is.EqualTo(responses[0].AccountId));
    }

    // The login that did not make the account is checked as one for an existing account.
    [Test]
    public void TheSecondLogin_ChecksThePassword() {
        LoginResponse[] responses = LoginTogether("secret", "other");

        Assert.That(responses.Count(response => response.Code == LoginResponse.Types.Code.Ok), Is.EqualTo(1));
        Assert.That(responses.Count(response => response.Code == LoginResponse.Types.Code.ErrorPassword), Is.EqualTo(1));
    }
}

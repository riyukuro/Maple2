using System;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Global.Service;
using Maple2.Server.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.World;

// Logins the world refuses before the character list: maintenance (result 11, the server's text) and a full server
// (result 6) at the channels' capacity. GMs pass both; a player already online is not refused as full.
public class LoginGateTests {
    private sealed class Gate : ILoginGate {
        public string? Maintenance { get; set; }
        public int Online;
        public int Channels = 1;
        public bool AccountIsOnline;

        public (int Online, int Channels) Load() => (Online, Channels);
        public bool AccountOnline(long accountId) => AccountIsOnline;
    }

    private DbContextOptions options = null!;
    private GameStorage storage = null!;
    private Gate gate = null!;
    private GlobalService service = null!;
    private readonly Guid machineId = Guid.NewGuid();

    [SetUp]
    public void SetUp() {
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        gate = new Gate();
        service = new GlobalService(storage, gate);
    }

    // The World host builds the service the way gRPC does (ActivatorUtilities.CreateFactory with no given arguments);
    // a second public constructor made every login throw "Multiple constructors accepting all given argument types".
    [Test]
    public void GrpcActivation_BuildsTheServiceWithTheContainersGate() {
        var services = new ServiceCollection();
        services.AddSingleton(storage);
        services.AddSingleton<ILoginGate>(gate);
        using ServiceProvider provider = services.BuildServiceProvider();
        ObjectFactory factory = ActivatorUtilities.CreateFactory(typeof(GlobalService), Type.EmptyTypes);

        object built = factory(provider, null);

        Assert.That(built, Is.InstanceOf<GlobalService>());
        gate.Maintenance = "checking";
        Account("gated", AdminPermissions.None);
        LoginResponse response = ((GlobalService) built).Login(new LoginRequest {
            Username = "gated",
            Password = "secret",
            MachineId = machineId.ToString(),
            ClientIp = "127.0.0.1",
        }, null!).Result;
        Assert.That(response.Code, Is.EqualTo(LoginResponse.Types.Code.Maintenance));
    }

    // The account with these permissions in any build: a Debug build's CreateAccount makes every account Admin
    // (GameStorage.CreateAccount), which let the player through both gates as a GM.
    private void Account(string name, AdminPermissions permissions) {
        long accountId;
        using (GameStorage.Request db = storage.Context()) {
            accountId = db.CreateAccount(new Account {
                Username = name,
                MachineId = machineId,
                AdminPermissions = permissions,
            }, "secret").Id;
        }
        using var context = new Ms2Context(options);
        context.Account.Find(accountId)!.Permissions = permissions.ToString();
        context.SaveChanges();
    }

    private LoginResponse Login(string name) {
        return service.Login(new LoginRequest {
            Username = name,
            Password = "secret",
            MachineId = machineId.ToString(),
            ClientIp = "127.0.0.1",
        }, null!).Result;
    }

    [Test]
    public void Rules() {
        Assert.That(LoginGateRules.Check(false, false, false, 10, 2, 5), Is.EqualTo(LoginGateRules.Refusal.Full));
        Assert.That(LoginGateRules.Check(false, false, false, 9, 2, 5), Is.EqualTo(LoginGateRules.Refusal.None));
        Assert.That(LoginGateRules.Check(false, false, true, 10, 2, 5), Is.EqualTo(LoginGateRules.Refusal.None));
        Assert.That(LoginGateRules.Check(false, false, false, 10, 0, 5), Is.EqualTo(LoginGateRules.Refusal.None));
        Assert.That(LoginGateRules.Check(false, false, false, 10, 2, 0), Is.EqualTo(LoginGateRules.Refusal.None));
        Assert.That(LoginGateRules.Check(false, true, true, 0, 2, 5), Is.EqualTo(LoginGateRules.Refusal.Maintenance));
        Assert.That(LoginGateRules.Check(true, true, false, 10, 2, 5), Is.EqualTo(LoginGateRules.Refusal.None));
        // No overflow at a large capacity.
        Assert.That(LoginGateRules.Check(false, false, false, int.MaxValue, 4, int.MaxValue), Is.EqualTo(LoginGateRules.Refusal.None));
    }

    [Test]
    public void Maintenance_RefusesAPlayerWithTheText_ButNotAGm() {
        Account("player", AdminPermissions.None);
        Account("gm", AdminPermissions.GameMaster);
        gate.Maintenance = "Back at 10:00";

        LoginResponse player = Login("player");
        Assert.That(player.Code, Is.EqualTo(LoginResponse.Types.Code.Maintenance));
        Assert.That(player.Message, Is.EqualTo("Back at 10:00"));
        Assert.That(player.AccountId, Is.Not.Zero);
        Assert.That(Login("gm").Code, Is.EqualTo(LoginResponse.Types.Code.Ok));

        gate.Maintenance = null;
        Assert.That(Login("player").Code, Is.EqualTo(LoginResponse.Types.Code.Ok));
    }

    [Test]
    public void AFullServer_RefusesANewPlayer_ButNotOneAlreadyOnlineOrAGm() {
        Account("player", AdminPermissions.None);
        Account("gm", AdminPermissions.Admin);
        gate.Online = Target.ChannelCapacity;

        Assert.That(Login("player").Code, Is.EqualTo(LoginResponse.Types.Code.FullServer));
        Assert.That(Login("gm").Code, Is.EqualTo(LoginResponse.Types.Code.Ok));
        gate.AccountIsOnline = true;
        Assert.That(Login("player").Code, Is.EqualTo(LoginResponse.Types.Code.Ok));
        gate.AccountIsOnline = false;
        gate.Online = Target.ChannelCapacity - 1;
        Assert.That(Login("player").Code, Is.EqualTo(LoginResponse.Types.Code.Ok));
    }

    // A refused login of a new name makes no account (AutoRegister): the refusal comes first, a new account is no GM.
    [Test]
    public void ARefusedNewName_MakesNoAccount() {
        gate.Maintenance = "Back at 10:00";
        Assert.That(Login("newcomer").Code, Is.EqualTo(LoginResponse.Types.Code.Maintenance));
        gate.Maintenance = null;
        gate.Online = Target.ChannelCapacity;
        Assert.That(Login("newcomer").Code, Is.EqualTo(LoginResponse.Types.Code.FullServer));
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.GetAccount("newcomer"), Is.Null);
        }

        gate.Online = 0;
        LoginResponse made = Login("newcomer");
        Assert.That(made.Code, Is.EqualTo(LoginResponse.Types.Code.Ok));
        Assert.That(made.AccountId, Is.Not.Zero);
    }

    // A wrong password is answered as before: maintenance tells nothing to someone who cannot log in.
    [Test]
    public void AWrongPassword_IsCheckedFirst() {
        Account("player", AdminPermissions.None);
        gate.Maintenance = "Back at 10:00";

        LoginResponse response = service.Login(new LoginRequest {
            Username = "player",
            Password = "other",
            MachineId = machineId.ToString(),
            ClientIp = "127.0.0.1",
        }, null!).Result;
        Assert.That(response.Code, Is.EqualTo(LoginResponse.Types.Code.ErrorPassword));
    }
}

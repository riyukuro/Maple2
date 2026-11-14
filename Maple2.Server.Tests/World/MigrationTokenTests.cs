using System;
using Grpc.Core;
using Maple2.Server.World.Service;
using Microsoft.Extensions.Caching.Memory;

namespace Maple2.Server.Tests.World;

// An account has one client, so only its newest token is live. Before, every token stayed valid until it was used
// or expired: a token left unused (a character picked by another login of the account before the first one arrived,
// or the client closed on the way) still let a second game session of the account in next to the first one.
public class MigrationTokenTests {
    private static readonly Guid Machine = Guid.NewGuid();

    private static WorldService World() {
        return new WorldService(new MemoryCache(new MemoryCacheOptions()), null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static ulong Out(WorldService world, long accountId, long characterId) {
        return world.MigrateOut(new MigrateOutRequest {
            AccountId = accountId,
            CharacterId = characterId,
            MachineId = Machine.ToString(),
            Server = Maple2.Server.World.Service.Server.Login,
        }, null!).Result.Token;
    }

    private static MigrateInResponse In(WorldService world, long accountId, ulong token) {
        return world.MigrateIn(new MigrateInRequest {
            AccountId = accountId,
            Token = token,
            MachineId = Machine.ToString(),
        }, null!).Result;
    }

    [Test]
    public void ANewToken_EndsTheAccountsEarlierOne() {
        WorldService world = World();
        ulong first = Out(world, 1, 11);
        ulong second = Out(world, 1, 12);

        var ex = Assert.Throws<RpcException>(() => In(world, 1, first))!;
        Assert.That(ex.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
        Assert.That(In(world, 1, second).CharacterId, Is.EqualTo(12));
    }

    [Test]
    public void AnotherAccountsToken_StaysLive() {
        WorldService world = World();
        ulong first = Out(world, 1, 11);
        Out(world, 2, 21);

        Assert.That(In(world, 1, first).CharacterId, Is.EqualTo(11));
    }

    [Test]
    public void AUsedToken_CannotBeUsedAgain_AndTheNextOneIsLive() {
        WorldService world = World();
        ulong first = Out(world, 1, 11);
        In(world, 1, first);

        Assert.Throws<RpcException>(() => In(world, 1, first));
        Assert.That(In(world, 1, Out(world, 1, 12)).CharacterId, Is.EqualTo(12));
    }
}

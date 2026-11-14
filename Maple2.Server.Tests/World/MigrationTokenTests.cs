using System;
using Grpc.Core;
using Maple2.Server.World.Service;
using Microsoft.Extensions.Caching.Memory;

namespace Maple2.Server.Tests.World;

// A character select ends the account's earlier tokens: only the move of its newest login is live. Before, every
// token stayed valid until it was used or expired: a token left unused (a character picked by another login of the
// account before the first one arrived, the client closed on the way) still let a second game session of the
// account in next to the first one. A move of a game session ends none: two moves of one session can overlap, and
// the client goes with the first one it was sent.
public class MigrationTokenTests {
    private static readonly Guid Machine = Guid.NewGuid();

    private static WorldService World() {
        return new WorldService(new MemoryCache(new MemoryCacheOptions()), null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static ulong Out(WorldService world, long accountId, long characterId, bool select = true) {
        return world.MigrateOut(new MigrateOutRequest {
            AccountId = accountId,
            CharacterId = characterId,
            MachineId = Machine.ToString(),
            Server = Maple2.Server.World.Service.Server.Login,
            EndsEarlier = select,
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
    public void ASelect_EndsTheAccountsEarlierTokens() {
        WorldService world = World();
        ulong first = Out(world, 1, 11);
        ulong moving = Out(world, 1, 11, select: false);
        ulong second = Out(world, 1, 12);

        Assert.Multiple(() => {
            Assert.That(Assert.Throws<RpcException>(() => In(world, 1, first))!.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
            Assert.That(Assert.Throws<RpcException>(() => In(world, 1, moving))!.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
            Assert.That(In(world, 1, second).CharacterId, Is.EqualTo(12));
        });
    }

    // A field's warp next to the client's own move: the client goes with the first GameToGame, and is let in.
    [Test]
    public void TwoMovesOfAGameSession_BothStayLive() {
        WorldService world = World();
        ulong first = Out(world, 1, 11, select: false);
        ulong second = Out(world, 1, 11, select: false);

        Assert.That(In(world, 1, first).CharacterId, Is.EqualTo(11));
        Assert.That(In(world, 1, second).CharacterId, Is.EqualTo(11));
    }

    // A game move after a select, then another select: the second select ends the game move too.
    [Test]
    public void ASelect_EndsAGameMoveIssuedAfterTheFirstSelect() {
        WorldService world = World();
        In(world, 1, Out(world, 1, 11));
        ulong moving = Out(world, 1, 11, select: false);
        ulong second = Out(world, 1, 12);

        Assert.That(Assert.Throws<RpcException>(() => In(world, 1, moving))!.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
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

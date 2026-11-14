using System.Collections.Generic;
using Maple2.Model.Error;
using Maple2.Model.Game.Pvp;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using Microsoft.Extensions.Caching.Memory;

namespace Maple2.Server.Tests.World;

// A party kick takes the kicked member out of a Maple Survival squad's queue only when the party took the kick:
// a kick the party refuses (no such party, not the leader, not a member) leaves every squad as it was.
public class PartyKickSquadTests {
    private readonly List<(long CharacterId, int Squad)> squadNotices = [];

    private PvpLookup Pvp() {
        return new PvpLookup((_, _, _, _) => (MigrationError.ok, 1, 0, 0), (characterId, _, _, squad) => {
            if (squad != 0) {
                squadNotices.Add((characterId, squad));
            }
        }, () => 0);
    }

    private static WorldService World(PvpLookup pvp) {
        return new WorldService(new MemoryCache(new MemoryCacheOptions()), null!, null!, null!, null!,
            new PartyLookup(null!, null!, null!, null!), null!, null!, null!, null!, null!, null!, null!, pvp, null!, null!, null!, null!);
    }

    [Test]
    public void RefusedKick_LeavesTheSquadQueued() {
        PvpLookup pvp = Pvp();
        WorldService world = World(pvp);
        Assert.That(pvp.ArenaRegisterSquad([1, 2], 100, 3, 3), Is.EqualTo(RankDuelError.none));
        squadNotices.Clear();

        PartyResponse response = world.Party(new PartyRequest {
            RequestorId = 3,
            Kick = new PartyRequest.Types.Kick { PartyId = 999, ReceiverId = 2 },
        }, null!).Result;

        Assert.That(response.Error, Is.EqualTo((int) PartyError.s_party_err_not_found));
        Assert.That(squadNotices, Is.Empty);
        // Still queued: the member's own cancel takes the squad out.
        Assert.That(pvp.ArenaUnregister(2), Is.EqualTo(RankDuelError.none));
    }
}

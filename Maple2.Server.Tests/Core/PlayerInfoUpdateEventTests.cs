using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Sync;

namespace Maple2.Server.Tests.Core;

// Clubs and dungeon limits in a player update are compared with the held ones by content.
public class PlayerInfoUpdateEventTests {
    private static PlayerInfo Held() {
        return new PlayerInfo(new CharacterInfo(1, 2, "a", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), [10, 11]) {
            DungeonEnterLimits = new Dictionary<int, DungeonEnterLimit> {
                [1] = DungeonEnterLimit.Rookie,
                [2] = DungeonEnterLimit.MinLevel,
            },
        };
    }

    private static PlayerUpdateRequest Request(long[] clubs, params (int Id, DungeonEnterLimit Limit)[] limits) {
        return new PlayerUpdateRequest {
            AccountId = 1,
            CharacterId = 2,
            Clubs = { clubs.Select(id => new ClubUpdate { Id = id }) },
            DungeonEnterLimits = { limits.Select(limit => new DungeonEnterLimitUpdate { DungeonId = limit.Id, Limit = (int) limit.Limit }) },
        };
    }

    private static UpdateField Type(PlayerUpdateRequest request) {
        return new PlayerInfoUpdateEvent(Held(), request).Type;
    }

    [Test]
    public void EqualClubsAndLimits_ChangeNothing() {
        Assert.That(Type(Request([10, 11], (1, DungeonEnterLimit.Rookie), (2, DungeonEnterLimit.MinLevel))), Is.EqualTo(UpdateField.None));
        Assert.That(Type(Request([10, 11], (2, DungeonEnterLimit.MinLevel))), Is.EqualTo(UpdateField.None));
    }

    // An hp update that repeats the clubs and limits is an hp update only.
    [Test]
    public void EqualClubsAndLimits_LeaveTheOtherFieldsAlone() {
        PlayerUpdateRequest request = Request([10, 11], (1, DungeonEnterLimit.Rookie));
        request.Health = new HealthUpdate { CurrentHp = 5, TotalHp = 9 };

        Assert.That(Type(request), Is.EqualTo(UpdateField.Health));
    }

    [Test]
    public void ChangedClubs_SetClubs() {
        Assert.That(Type(Request([10, 12])), Is.EqualTo(UpdateField.Clubs));
        Assert.That(Type(Request([10])), Is.EqualTo(UpdateField.Clubs));
        Assert.That(Type(Request([10, 11, 12])), Is.EqualTo(UpdateField.Clubs));
    }

    [Test]
    public void ChangedOrNewLimit_SetsDungeon() {
        Assert.That(Type(Request([], (1, DungeonEnterLimit.MinLevel))), Is.EqualTo(UpdateField.Dungeon));
        Assert.That(Type(Request([], (3, DungeonEnterLimit.Rookie))), Is.EqualTo(UpdateField.Dungeon));
        Assert.That(Type(Request([10, 12], (3, DungeonEnterLimit.Rookie))), Is.EqualTo(UpdateField.Clubs | UpdateField.Dungeon));
    }

    // A member's copy keeps its own tables, so an update merged into the held record is still seen as a change
    // when the copy is brought up to date.
    [Test]
    public void Clone_HoldsItsOwnClubsAndLimits() {
        PlayerInfo held = Held();
        PlayerInfo copy = held.Clone();
        var update = new PlayerInfoUpdateEvent(held, Request([10, 12], (1, DungeonEnterLimit.MinLevel)));

        held.Update(update);

        Assert.That(held.DungeonEnterLimits[1], Is.EqualTo(DungeonEnterLimit.MinLevel));
        Assert.That(copy.DungeonEnterLimits[1], Is.EqualTo(DungeonEnterLimit.Rookie));
        Assert.That(copy.ClubIds, Is.EqualTo(new long[] { 10, 11 }));

        copy.Update(update.Type, held);
        held.Update(new PlayerInfoUpdateEvent(held, Request([], (1, DungeonEnterLimit.Veteran))));

        Assert.That(copy.DungeonEnterLimits[1], Is.EqualTo(DungeonEnterLimit.MinLevel));
        Assert.That(copy.ClubIds, Is.EqualTo(new long[] { 10, 12 }));
    }
}

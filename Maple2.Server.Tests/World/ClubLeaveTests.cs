using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using Club = Maple2.Model.Game.Club.Club;

namespace Maple2.Server.Tests.World;

// A member leaves a club of three on the World server. When the leader leaves, the next member leads (KMS2 sends
// UpdateLeader, then the leave notice), and the stored club names that member, so it loads again.
public class ClubLeaveTests {
    private sealed class Players(Dictionary<long, PlayerInfo> infos) : IPlayerInfoProvider {
        public PlayerInfo? GetPlayerInfo(long id) => infos.GetValueOrDefault(id);
    }

    private TestGameSession test = null!;
    private Players players = null!;
    private ClubManager manager = null!;
    private long[] ids = null!;

    private static PlayerInfo Info(long characterId, string name) {
        return new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
    }

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        // The member row goes by a batch delete, which needs a context it can open on the test database.
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = _ => new Ms2Context(test.Options);
        ids = [test.CharacterId, test.CharacterId + 500, test.CharacterId + 501];
        var infos = new Dictionary<long, PlayerInfo> {
            [ids[0]] = Info(ids[0], "Leader"),
            [ids[1]] = Info(ids[1], "Second"),
            [ids[2]] = Info(ids[2], "Third"),
        };
        players = new Players(infos);
        Club club;
        using (GameStorage.Request db = test.Storage.Context()) {
            club = db.CreateClub(players, "Club", ids[0], infos.Values.ToList())!;
        }
        Assume.That(club, Is.Not.Null);
        manager = new ClubManager(club) {
            GameStorage = test.Storage,
            ChannelClients = new ChannelClientLookup(),
        };
    }

    [TearDown]
    public void TearDown() {
        manager.Dispose();
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = null;
    }

    private Club? Stored() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetClub(players, manager.Club.Id);
    }

    [Test]
    public void LeaderLeaves_TheStoredClubNamesTheNewLeader() {
        Assert.That(manager.Leave(ids[0]), Is.EqualTo(ClubError.none));

        Assert.That(manager.Club.LeaderId, Is.Not.EqualTo(ids[0]));
        Club? stored = Stored();
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.LeaderId, Is.EqualTo(manager.Club.LeaderId));
        Assert.That(stored.Leader.CharacterId, Is.EqualTo(manager.Club.LeaderId));
        Assert.That(stored.Members.Keys, Is.EquivalentTo(ids.Skip(1)));
    }

    [Test]
    public void MemberLeaves_TheLeaderStays() {
        Assert.That(manager.Leave(ids[2]), Is.EqualTo(ClubError.none));

        Club? stored = Stored();
        Assert.That(stored!.LeaderId, Is.EqualTo(ids[0]));
        Assert.That(stored.Members.Keys, Is.EquivalentTo(ids.Take(2)));
    }
}

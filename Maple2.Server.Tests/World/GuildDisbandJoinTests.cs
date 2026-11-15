using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// A disband (GuildLookup.Disband) checks that only the leader is left and closes the guild to joins in one step
// under the guild lock (GuildManager.TryClose). Before, the check ran without the lock: a join between the check
// and the delete added a member row to a guild that was then deleted.
public class GuildDisbandJoinTests {
    private TestGameSession test = null!;
    private Guild guild = null!;
    private GuildManager manager = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        property = property with { Level = 1, Capacity = 10 };
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property }));
        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Closing", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
        }
        guild.Members[test.CharacterId] = new GuildMember { GuildId = guild.Id, Info = Info(test.CharacterId, "Leader"), Rank = 0 };
        manager = new GuildManager(guild) {
            GameStorage = test.Storage,
            ChannelClients = new ChannelClientLookup(),
            TableMetadata = test.TableMetadata,
            ItemMetadata = test.ItemMetadata,
            ServerTableMetadata = test.ServerTableMetadata,
        };
    }

    [TearDown]
    public void TearDown() {
        manager.Dispose();
    }

    private static PlayerInfo Info(long characterId, string name) =>
        new(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "", new AchievementInfo(), []);

    // The join that comes after the disband's check is refused, and no member row is made.
    [Test]
    public void AJoinAfterTheDisbandCheck_IsRefused() {
        Assert.That(manager.TryClose(), Is.True);

        GuildError error = manager.Join("Leader", Info(test.CharacterId + 1, "Late"));

        Assert.Multiple(() => {
            Assert.That(error, Is.EqualTo(GuildError.s_guild_err_null_guild));
            Assert.That(guild.Members.Keys, Is.EqualTo(new[] { test.CharacterId }));
        });
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetGuildMembers(new NoPlayers(), guild.Id).Select(member => member.CharacterId), Does.Not.Contain(test.CharacterId + 1));
    }

    // A guild with another member is not closed, and still takes joins.
    [Test]
    public void AGuildWithMembers_StaysOpen() {
        Assert.That(manager.Join("Leader", Info(test.CharacterId + 1, "Member")), Is.EqualTo(GuildError.none));

        Assert.That(manager.TryClose(), Is.False);
        Assert.That(manager.Join("Leader", Info(test.CharacterId + 2, "Next")), Is.EqualTo(GuildError.none));
    }

    private sealed class NoPlayers : IPlayerInfoProvider {
        public PlayerInfo? GetPlayerInfo(long id) => null;
    }
}

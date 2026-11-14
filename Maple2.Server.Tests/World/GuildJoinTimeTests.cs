using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.World;

// A guild member's join time is the time the row was made, in the server's clock: the client refuses the check-in
// and the donation on the join day (IsNotRecentDayChange, timeutil.cpp:344, which also refuses a join time that is
// not yet past) and the server refuses the check-in until the day after. The column's own default is in the
// database's time zone and read back as local time, a join time ahead by the UTC offset.
public class GuildJoinTimeTests {
    private sealed class Players(Dictionary<long, PlayerInfo> infos) : IPlayerInfoProvider {
        public PlayerInfo? GetPlayerInfo(long id) => infos.GetValueOrDefault(id);
    }

    private static PlayerInfo Info(long accountId, long characterId, string name) =>
        new(new CharacterInfo(accountId, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "", new AchievementInfo(), []);

    private static void AssertNow(long joinTime, long before, long after) {
        Assert.That(joinTime, Is.InRange(before, after));
    }

    [Test]
    public void TheLeaderAndANewMemberJoinNow() {
        var test = new TestGameSession();
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        PlayerInfo leader = Info(test.AccountId, test.CharacterId, "Leader");
        PlayerInfo joiner = Info(test.AccountId, test.CharacterId + 1, "Member");
        IList<GuildMember> members;
        using (GameStorage.Request db = test.Storage.Context()) {
            Guild guild = db.CreateGuild("Joined", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
            Assert.That(db.CreateGuildMember(guild.Id, joiner), Is.Not.Null);
            members = db.GetGuildMembers(new Players(new Dictionary<long, PlayerInfo> {
                [leader.CharacterId] = leader,
                [joiner.CharacterId] = joiner,
            }), guild.Id);
        }
        long after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.That(members.Select(member => member.CharacterId), Is.EquivalentTo(new[] { leader.CharacterId, joiner.CharacterId }));
        foreach (GuildMember member in members) {
            AssertNow(member.JoinTime, before, after);
        }
    }

    // A save that finds a member with no row yet makes the row with the join time now.
    [Test]
    public void ASavedMemberWithNoRowJoinsNow() {
        var test = new TestGameSession();
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        PlayerInfo leader = Info(test.AccountId, test.CharacterId, "Leader");
        PlayerInfo joiner = Info(test.AccountId, test.CharacterId + 1, "Member");
        IList<GuildMember> members;
        long guildId;
        using (GameStorage.Request db = test.Storage.Context()) {
            Guild guild = db.CreateGuild("Saved", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
            guildId = guild.Id;
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveGuildMembers(guildId, new[] {
                new GuildMember { GuildId = guildId, Info = leader, Rank = 0 },
                new GuildMember { GuildId = guildId, Info = joiner, Rank = 5 },
            }), Is.True);
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            members = db.GetGuildMembers(new Players(new Dictionary<long, PlayerInfo> {
                [leader.CharacterId] = leader,
                [joiner.CharacterId] = joiner,
            }), guildId);
        }
        long after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        GuildMember saved = members.Single(member => member.CharacterId == joiner.CharacterId);
        AssertNow(saved.JoinTime, before, after);
    }

    // A first check-in (the check-in time is still the epoch) is refused on the join day and taken from the next day
    // on, also by a member who joined a minute before midnight.
    [Test]
    public void AFirstCheckInIsTakenFromTheDayAfterTheJoin() {
        long today = GuildRules.DayStart();
        Assert.That(GuildRules.CanCheckIn(today + 60, 0, today), Is.False);
        Assert.That(GuildRules.CanCheckIn(today - 60, 0, today), Is.True);
    }
}

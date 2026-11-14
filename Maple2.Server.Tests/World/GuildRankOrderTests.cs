using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// Guild 8 (expel), Guild 10 (set a rank) and Guild 63 (new master) in the world: the client asks only for the right, so
// the world keeps the order of the ranks (a member acts only on a lower rank and gives no rank at or above its own), and
// a rank or master change is stored at once.
public class GuildRankOrderTests {
    private TestGameSession test = null!;
    private Guild guild = null!;
    private GuildManager manager = null!;
    private GuildMember master = null!;
    private GuildMember officer = null!; // rank 2, every right
    private GuildMember peer = null!;    // rank 2
    private GuildMember senior = null!;  // rank 1
    private GuildMember junior = null!;  // rank 3

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        // The member delete is a batch delete (Z.EntityFramework.Plus), which needs a context factory in memory.
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = _ => new Ms2Context(test.Options);
        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Guild", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
        }
        master = Member(test.CharacterId, "Master", 0);
        officer = Member(test.CharacterId + 501, "Officer", 2);
        peer = Member(test.CharacterId + 502, "Peer", 2);
        senior = Member(test.CharacterId + 503, "Senior", 1);
        junior = Member(test.CharacterId + 504, "Junior", 3);
        guild.Members.Clear();
        foreach (GuildMember member in new[] { master, officer, peer, senior, junior }) {
            guild.Members[member.CharacterId] = member;
        }
        guild.LeaderCharacterId = master.CharacterId;
        guild.Ranks[2].Permission = GuildPermission.All;
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveGuild(guild), Is.True);
        }
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
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = null;
        manager.Dispose();
        master.Dispose();
        officer.Dispose();
        peer.Dispose();
        senior.Dispose();
        junior.Dispose();
    }

    private GuildMember Member(long characterId, string name, byte rank) {
        var info = new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []) {
            Channel = -1,
        };
        return new GuildMember { GuildId = guild.Id, Info = info, Rank = rank, JoinTime = 1000 };
    }

    private byte StoredRank(GuildMember member) {
        using var context = new Ms2Context(test.Options);
        return context.GuildMember.Single(row => row.GuildId == guild.Id && row.CharacterId == member.CharacterId).Rank;
    }

    [Test]
    public void SetRank_NotOwnRank() {
        Assert.That(manager.UpdateMember(officer.CharacterId, officer.CharacterId, 1, null), Is.EqualTo(GuildError.s_guild_err_no_authority));
        Assert.That(officer.Rank, Is.EqualTo(2));
        Assert.That(StoredRank(officer), Is.EqualTo(2));
    }

    [TestCase((byte) 1)]
    [TestCase((byte) 2)]
    public void SetRank_NotAtOrAboveOwnRank(byte rank) {
        Assert.That(manager.UpdateMember(officer.CharacterId, junior.CharacterId, rank, null), Is.EqualTo(GuildError.s_guild_err_invalid_grade_range));
        Assert.That(junior.Rank, Is.EqualTo(3));
    }

    [Test]
    public void SetRank_NotOfAPeerOrAHigherRank() {
        Assert.That(manager.UpdateMember(officer.CharacterId, peer.CharacterId, 4, null), Is.EqualTo(GuildError.s_guild_err_no_authority));
        Assert.That(manager.UpdateMember(officer.CharacterId, senior.CharacterId, 4, null), Is.EqualTo(GuildError.s_guild_err_no_authority));
        Assert.That(peer.Rank, Is.EqualTo(2));
        Assert.That(senior.Rank, Is.EqualTo(1));
    }

    [Test]
    public void SetRank_BelowOwnRankIsStored() {
        Assert.That(manager.UpdateMember(officer.CharacterId, junior.CharacterId, 4, null), Is.EqualTo(GuildError.none));
        Assert.That(junior.Rank, Is.EqualTo(4));
        Assert.That(StoredRank(junior), Is.EqualTo(4));

        Assert.That(manager.UpdateMember(master.CharacterId, junior.CharacterId, 1, null), Is.EqualTo(GuildError.none));
        Assert.That(StoredRank(junior), Is.EqualTo(1));
    }

    [Test]
    public void Expel_NotAPeerNorItself() {
        Assert.That(manager.Expel(officer.CharacterId, peer.CharacterId), Is.EqualTo(GuildError.s_guild_err_no_authority));
        Assert.That(manager.Expel(officer.CharacterId, officer.CharacterId), Is.EqualTo(GuildError.s_guild_err_no_authority));
        Assert.That(guild.Members.ContainsKey(peer.CharacterId) && guild.Members.ContainsKey(officer.CharacterId), Is.True);

        Assert.That(manager.Expel(officer.CharacterId, junior.CharacterId), Is.EqualTo(GuildError.none));
        Assert.That(guild.Members.ContainsKey(junior.CharacterId), Is.False);
    }

    [Test]
    public void NewMaster_IsStored() {
        Assert.That(manager.UpdateLeader(master.CharacterId, officer.CharacterId), Is.EqualTo(GuildError.none));
        Assert.That(guild.LeaderCharacterId, Is.EqualTo(officer.CharacterId));

        using var context = new Ms2Context(test.Options);
        Assert.That(context.Guild.Single(row => row.Id == guild.Id).LeaderId, Is.EqualTo(officer.CharacterId));
        Assert.That(StoredRank(officer), Is.EqualTo(0));
        Assert.That(StoredRank(master), Is.EqualTo(1));
    }

    [Test]
    public void NewMaster_NotTheMasterItself() {
        Assert.That(manager.UpdateLeader(master.CharacterId, master.CharacterId), Is.Not.EqualTo(GuildError.none));
        Assert.That(master.Rank, Is.EqualTo(0));
        Assert.That(guild.LeaderCharacterId, Is.EqualTo(master.CharacterId));
    }

    // A failed write keeps the rank in memory as it was (the member row is gone, so the save finds nothing).
    [Test]
    public void SetRank_FailedSave_KeepsTheRank() {
        using (var context = new Ms2Context(test.Options)) {
            context.GuildMember.Remove(context.GuildMember.Single(row => row.GuildId == guild.Id && row.CharacterId == junior.CharacterId));
            context.SaveChanges();
        }

        Assert.That(manager.UpdateMember(officer.CharacterId, junior.CharacterId, 4, null), Is.EqualTo(GuildError.s_guild_err_unknown));
        Assert.That(junior.Rank, Is.EqualTo(3));
    }

    // A failed write keeps the old master, both ranks and the leader fields (the guild row is gone).
    [Test]
    public void NewMaster_FailedSave_KeepsTheMaster() {
        using (var context = new Ms2Context(test.Options)) {
            context.Guild.Remove(context.Guild.Single(row => row.Id == guild.Id));
            context.SaveChanges();
        }

        Assert.That(manager.UpdateLeader(master.CharacterId, officer.CharacterId), Is.EqualTo(GuildError.s_guild_err_unknown));
        Assert.That(guild.LeaderCharacterId, Is.EqualTo(master.CharacterId));
        Assert.That(master.Rank, Is.EqualTo(0));
        Assert.That(officer.Rank, Is.EqualTo(2));
    }
}

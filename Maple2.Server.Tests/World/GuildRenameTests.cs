using System;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// The guild rename on the World server (a rename ticket): only the master, and only to a name no guild has. The
// new name is stored.
public class GuildRenameTests {
    private TestGameSession test = null!;
    private Guild guild = null!;
    private GuildManager manager = null!;
    private GuildMember master = null!;
    private GuildMember member = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Old", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
            db.CreateGuild("Taken", test.CharacterId + 900);
        }
        master = Member(test.CharacterId, "Master", 0);
        member = Member(test.CharacterId + 500, "Member", 1);
        guild.Members[master.CharacterId] = master;
        guild.Members[member.CharacterId] = member;
        manager = new GuildManager(guild) {
            GameStorage = test.Storage,
            ChannelClients = new ChannelClientLookup(),
            TableMetadata = test.TableMetadata,
        };
    }

    [TearDown]
    public void TearDown() {
        manager.Dispose();
        master.Dispose();
        member.Dispose();
    }

    private GuildMember Member(long characterId, string name, byte rank) {
        var info = new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        return new GuildMember { GuildId = guild.Id, Info = info, Rank = rank, JoinTime = 1000 };
    }

    private string StoredName() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetGuild(guild.Id)!.Name;
    }

    [TestCase("stranger", "New", GuildError.s_guild_err_null_member)]
    [TestCase("member", "New", GuildError.s_guild_err_no_master)]
    [TestCase("master", "Taken", GuildError.s_guild_err_name_exist)]
    [TestCase("master", "Old", GuildError.s_guild_err_name_exist)]
    public void Refused(string who, string name, GuildError expected) {
        long requestor = who switch {
            "master" => master.CharacterId,
            "member" => member.CharacterId,
            _ => 999,
        };

        Assert.That(manager.Rename(requestor, name), Is.EqualTo(expected));
        Assert.That(guild.Name, Is.EqualTo("Old"));
        Assert.That(StoredName(), Is.EqualTo("Old"));
    }

    [Test]
    public void Renamed() {
        Assert.That(manager.Rename(master.CharacterId, "New"), Is.EqualTo(GuildError.none));
        Assert.That(guild.Name, Is.EqualTo("New"));
        Assert.That(StoredName(), Is.EqualTo("New"));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GuildExists(guildName: "Old"), Is.False);
    }
}

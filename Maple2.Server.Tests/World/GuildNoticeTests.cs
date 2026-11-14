using System;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// The guild notice on the World server (Guild 64): only a rank with the notice right, at most 300 characters, and
// the new notice is stored at once (before, only at the next save of the row for another change).
public class GuildNoticeTests {
    private TestGameSession test = null!;
    private Guild guild = null!;
    private GuildManager manager = null!;
    private GuildMember master = null!;
    private GuildMember member = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Notice", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
        }
        master = Member(test.CharacterId, "Master", 0);
        member = Member(test.CharacterId + 500, "Member", (byte) (guild.Ranks.Count - 1));
        guild.Members[master.CharacterId] = master;
        guild.Members[member.CharacterId] = member;
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
        master.Dispose();
        member.Dispose();
    }

    private GuildMember Member(long characterId, string name, byte rank) {
        var info = new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        return new GuildMember { GuildId = guild.Id, Info = info, Rank = rank, JoinTime = 1000 };
    }

    private string StoredNotice() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetGuild(guild.Id)!.Notice;
    }

    [Test]
    public void Updated_IsStored() {
        Assert.That(manager.UpdateNotice(master.CharacterId, "Raid at nine"), Is.EqualTo(GuildError.none));
        Assert.That(guild.Notice, Is.EqualTo("Raid at nine"));
        Assert.That(StoredNotice(), Is.EqualTo("Raid at nine"));
    }

    [Test]
    public void Refused_NothingStored() {
        Assert.That(guild.Ranks[member.Rank].Permission.HasFlag(GuildPermission.EditNotice), Is.False);
        Assert.Multiple(() => {
            Assert.That(manager.UpdateNotice(999, "x"), Is.EqualTo(GuildError.s_guild_err_null_member));
            Assert.That(manager.UpdateNotice(member.CharacterId, "x"), Is.EqualTo(GuildError.s_guild_err_no_authority));
            Assert.That(manager.UpdateNotice(master.CharacterId, new string('a', GuildRules.MaxNoticeLength + 1)),
                Is.EqualTo(GuildError.s_guild_err_unknown));
            Assert.That(guild.Notice, Is.Empty);
            Assert.That(StoredNotice(), Is.Empty);
        });
    }
}

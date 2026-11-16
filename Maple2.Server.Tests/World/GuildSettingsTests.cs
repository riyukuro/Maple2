using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.World;

// The guild's settings on the World server: rank (Guild 67), focus (68), emblem (65), poster, and the mini game
// event (99, KR table/guildevent.xml <guildEvent id="1" mapCode="66000001" />). Each is refused without its right,
// and a kept change is in the stored row (the search and a World restart read it).
public class GuildSettingsTests {
    private TestGameSession test = null!;
    private Guild guild = null!;
    private GuildManager manager = null!;
    private GuildMember master = null!;
    private GuildMember member = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        property = property with { Level = 1, Capacity = 10, FundMax = 1000 };
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property },
            Events: new Dictionary<int, GuildTable.Event> { [1] = new(1, 66000001) }));

        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Settings", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
        }
        master = Member(test.CharacterId, "Master", 0);
        member = Member(test.CharacterId + 500, "Member", 2);
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

    private Guild Stored() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetGuild(guild.Id)!;
    }

    [Test]
    public void UpdateRank_TheMastersChangeIsStored() {
        const GuildPermission permission = GuildPermission.Default | GuildPermission.InviteMembers;

        Assert.That(manager.UpdateRank(master.CharacterId, 2, "Elder", permission), Is.EqualTo(GuildError.none));

        GuildRank stored = Stored().Ranks.Single(rank => rank.Id == 2);
        Assert.That((stored.Name, stored.Permission), Is.EqualTo(("Elder", permission)));
    }

    // A member who is not the master, a rank id the guild lacks, a permission above the 12 rights (4095).
    [TestCase(false, 2, 1, GuildError.s_guild_err_no_authority)]
    [TestCase(true, 6, 1, GuildError.s_guild_err_invalid_grade_index)]
    [TestCase(true, 2, 4096, GuildError.s_guild_err_invalid_grade_data)]
    public void UpdateRank_Refused_ChangesNothing(bool asMaster, int rankId, int permission, GuildError error) {
        string before = guild.Ranks.Single(rank => rank.Id == 2).Name;

        Assert.That(manager.UpdateRank(asMaster ? master.CharacterId : member.CharacterId, rankId, "Elder", (GuildPermission) permission),
            Is.EqualTo(error));

        Assert.That(guild.Ranks.Single(rank => rank.Id == 2).Name, Is.EqualTo(before));
    }

    [Test]
    public void UpdateFocus_IsStoredForTheSearch() {
        Assert.That(manager.UpdateFocus(master.CharacterId, true, 0x0003), Is.EqualTo(GuildError.none));

        Assert.That((Stored().Listed, (int) Stored().Focus), Is.EqualTo((true, 0x0003)));
    }

    // Rank 2 holds only the default right, not the invite right the client asks for; a focus above the client's
    // 16 bits (GuildRules.FocusMask) is refused.
    [Test]
    public void UpdateFocus_Refused_ChangesNothing() {
        Assert.That(manager.UpdateFocus(member.CharacterId, true, 0x0003), Is.EqualTo(GuildError.s_guild_err_no_authority));
        Assert.That(manager.UpdateFocus(master.CharacterId, true, GuildRules.FocusMask + 1), Is.EqualTo(GuildError.s_guild_err_unknown));

        Assert.That((guild.Listed, (int) guild.Focus), Is.EqualTo((Stored().Listed, (int) Stored().Focus)));
    }

    [Test]
    public void UpdateEmblem_NeedsTheEmblemRight_AndIsStored() {
        Assert.That(manager.UpdateEmblem(member.CharacterId, "member.png"), Is.EqualTo(GuildError.s_guild_err_no_authority));
        Assert.That(manager.UpdateEmblem(master.CharacterId, "master.png"), Is.EqualTo(GuildError.none));

        Assert.That((guild.Emblem, Stored().Emblem), Is.EqualTo(("master.png", "master.png")));
    }

    private void FailGuildSaves() {
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Guild>().Any(entry => entry.State == EntityState.Modified);
    }

    // A save the database refuses: the rank, the focus and the emblem keep their old values, here and stored, and
    // the change is answered s_guild_err_unknown (no broadcast of a change that is not kept).
    [Test]
    public void SettingsNotStored_KeepTheOldValues() {
        GuildRank rank = guild.Ranks.Single(r => r.Id == 2);
        (string name, GuildPermission permission) = (rank.Name, rank.Permission);
        (bool listed, GuildFocus focus, string emblem) = (guild.Listed, guild.Focus, guild.Emblem);
        FailGuildSaves();

        Assert.Multiple(() => {
            Assert.That(manager.UpdateRank(master.CharacterId, 2, "Elder", GuildPermission.All), Is.EqualTo(GuildError.s_guild_err_unknown));
            Assert.That(manager.UpdateFocus(master.CharacterId, !listed, 0x0003), Is.EqualTo(GuildError.s_guild_err_unknown));
            Assert.That(manager.UpdateEmblem(master.CharacterId, "master.png"), Is.EqualTo(GuildError.s_guild_err_unknown));
        });
        test.FailSave = null;

        Guild stored = Stored();
        GuildRank storedRank = stored.Ranks.Single(r => r.Id == 2);
        Assert.Multiple(() => {
            Assert.That((rank.Name, rank.Permission, storedRank.Name, storedRank.Permission), Is.EqualTo((name, permission, name, permission)));
            Assert.That((guild.Listed, guild.Focus, stored.Listed, stored.Focus), Is.EqualTo((listed, focus, listed, focus)));
            Assert.That((guild.Emblem, stored.Emblem), Is.EqualTo((emblem, emblem)));
        });
    }

    // Any member may put up a poster (the client asks for no right); one of the same id is replaced. Both are in
    // the stored row: a poster is not lost on a World restart before the next guild save.
    [Test]
    public void AddOrUpdatePoster_AnyMember_ReplacesAndIsStored() {
        Assert.That(manager.AddOrUpdatePoster(1, "first.png", member.CharacterId, member.Name, 11), Is.EqualTo(GuildError.none));
        Assert.That(manager.AddOrUpdatePoster(1, "second.png", master.CharacterId, master.Name, 12), Is.EqualTo(GuildError.none));
        Assert.That(manager.AddOrUpdatePoster(1, "stranger.png", 999, "Stranger", 13), Is.EqualTo(GuildError.s_guild_err_null_member));

        Assert.That(guild.Posters.Select(poster => (poster.Id, poster.Picture, poster.OwnerId)),
            Is.EqualTo(new[] { (1, "second.png", master.CharacterId) }));
        Assert.That(Stored().Posters.Select(poster => (poster.Id, poster.Picture, poster.OwnerId, poster.OwnerName)),
            Is.EqualTo(new[] { (1, "second.png", master.CharacterId, "Master") }));
    }

    [Test]
    public void AddOrUpdatePoster_NotStored_KeepsTheOldPoster() {
        Assert.That(manager.AddOrUpdatePoster(1, "first.png", member.CharacterId, member.Name, 11), Is.EqualTo(GuildError.none));
        FailGuildSaves();

        Assert.That(manager.AddOrUpdatePoster(1, "second.png", master.CharacterId, master.Name, 12), Is.EqualTo(GuildError.s_guild_err_unknown));
        Assert.That(manager.AddOrUpdatePoster(2, "other.png", master.CharacterId, master.Name, 13), Is.EqualTo(GuildError.s_guild_err_unknown));
        test.FailSave = null;

        Assert.That(guild.Posters.Select(poster => (poster.Id, poster.Picture)), Is.EqualTo(new[] { (1, "first.png") }));
        Assert.That(Stored().Posters.Select(poster => (poster.Id, poster.Picture)), Is.EqualTo(new[] { (1, "first.png") }));
    }

    // KR event 1: opened by the master of a guild a day old, it is open for 30 seconds; a second open in that time,
    // an event without a row, a member without the mini game right and a guild under a day old are refused.
    [Test]
    public void OpenEvent_OpensOnceWhileOpen() {
        guild.CreationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 2 * 86400;
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.That(manager.OpenEvent(member.CharacterId, 1), Is.EqualTo(GuildError.s_guild_err_no_authority));
        Assert.That(manager.OpenEvent(master.CharacterId, 2), Is.EqualTo(GuildError.s_guild_err_unknown));
        Assert.That(manager.OpenEvent(master.CharacterId, 1), Is.EqualTo(GuildError.none));
        Assert.That(manager.OpenEvent(master.CharacterId, 1), Is.EqualTo(GuildError.s_guild_err_unknown));

        GuildEvent opened = manager.OpenedEvents().Single();
        Assert.That(opened.Id, Is.EqualTo(1));
        Assert.That(opened.Value, Is.InRange(before, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    [Test]
    public void OpenEvent_GuildUnderADayOld_IsRefused() {
        guild.CreationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600;

        Assert.That(manager.OpenEvent(master.CharacterId, 1), Is.EqualTo(GuildError.s_guild_err_unknown));
        Assert.That(manager.OpenedEvents(), Is.Empty);
    }
}

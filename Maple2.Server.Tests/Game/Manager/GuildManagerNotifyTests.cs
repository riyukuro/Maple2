using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;
using GuildManager = Maple2.Server.Game.Manager.GuildManager;

namespace Maple2.Server.Tests.Game.Manager;

// What the guild window of a member gets when the World tells this channel of a change: each notice is the
// client's packet for it, the held copy of the guild changes with it, and a notice for a guild or member the
// copy does not know is dropped without a packet.
public class GuildManagerNotifyTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private GuildManager manager = null!;
    private Guild guild = null!;
    private GuildMember self = null!;
    private GuildMember other = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property>()));
        test.Start();
        session = test.Session;
        manager = Build();
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        manager?.Dispose();
        self?.Dispose();
        other?.Dispose();
    }

    private GuildManager Build() {
        var created = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        typeof(GuildManager).GetField("session", Any)!.SetValue(created, session);
        typeof(GuildManager).GetField("tokenSource", Any)!.SetValue(created, new CancellationTokenSource());
        typeof(GuildManager).GetField("logger", Any)!.SetValue(created, Log.Logger);
        return created;
    }

    private static GuildMember Member(long accountId, long characterId, string name, byte rank) => new() {
        GuildId = 7,
        Info = new PlayerInfo(new CharacterInfo(accountId, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []),
        Rank = rank,
        JoinTime = 1000,
    };

    private void Join() {
        self = Member(session.AccountId, session.CharacterId, "Self", 0);
        other = Member(session.AccountId + 1, session.CharacterId + 1, "Other", 1);
        guild = new Guild(7, "Guild", self);
        guild.Members[self.CharacterId] = self;
        guild.Members[other.CharacterId] = other;
        guild.Ranks.Add(new GuildRank { Id = 0, Name = "Master", Permission = GuildPermission.All });
        guild.Ranks.Add(new GuildRank { Id = 1, Name = "Jr", Permission = (GuildPermission) 0 });
        typeof(GuildManager).GetProperty("Guild", Any)!.SetValue(manager, guild);
        session.Guild = manager;
    }

    private string[] Answers() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Guild).Select(Convert.ToHexString).ToArray();
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void NoGuild_EveryNoticeIsDroppedWithoutAPacket() {
        Assert.That(manager.UpdateLeader(1, 2), Is.False);
        Assert.That(manager.RemoveMember(1), Is.False);
        Assert.That(manager.UpdateMemberRank(1, 2, 1), Is.False);
        Assert.That(manager.UpdateMemberMessage(1, "x"), Is.False);
        Assert.That(manager.UseBuff("A", new GuildBuff(1, 1, 0), 0, 0, 0), Is.False);
        Assert.That(manager.OpenEvent("A", new GuildEvent(1, 1)), Is.False);
        Assert.That(manager.UpgradeBuff("A", new GuildBuff(1, 1, 0)), Is.False);
        Assert.That(manager.UpgradeNpc("A", new GuildNpc(GuildNpcType.Goods, 1)), Is.False);
        Assert.That(manager.UpdateHouse("A", 1, 1), Is.False);
        Assert.That(manager.UpdateNotice("A", "x"), Is.False);
        Assert.That(manager.UpdateRank("A", 1, "x", (GuildPermission) 0), Is.False);
        Assert.That(manager.UpdateFocus("A", true, GuildFocus.Social), Is.False);
        Assert.That(manager.Rename("x"), Is.False);
        Assert.That(manager.UpdateEmblem("A", "e"), Is.False);
        Assert.That(manager.AddOrUpdatePoster(new GuildPoster { Id = 1 }), Is.False);
        manager.WeeklyReset();

        Assert.That(Answers(), Is.Empty);
    }

    [Test]
    public void UpdateLeader_SwapsRanksAndNamesTheNewMaster() {
        Join();

        Assert.That(manager.UpdateLeader(self.CharacterId, other.CharacterId), Is.True);

        Assert.That(self.Rank, Is.EqualTo(1));
        Assert.That(other.Rank, Is.EqualTo(0));
        Assert.That(guild.LeaderCharacterId, Is.EqualTo(other.CharacterId));
        Assert.That(guild.LeaderAccountId, Is.EqualTo(other.AccountId));
        Assert.That(guild.LeaderName, Is.EqualTo("Other"));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyUpdateLeader("Self", "Other")) }));
    }

    [Test]
    public void UpdateLeader_UnknownMember_ChangesNothing() {
        Join();

        Assert.That(manager.UpdateLeader(self.CharacterId, 999), Is.False);
        Assert.That(manager.UpdateLeader(999, other.CharacterId), Is.False);

        Assert.That(self.Rank, Is.EqualTo(0));
        Assert.That(other.Rank, Is.EqualTo(1));
        Assert.That(Answers(), Is.Empty);
    }

    [Test]
    public void RemoveMember_Leave_IsTheLeaveNotice() {
        Join();

        Assert.That(manager.RemoveMember(other.CharacterId), Is.True);

        Assert.That(guild.Members.ContainsKey(other.CharacterId), Is.False);
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyLeave("Other")) }));
    }

    [Test]
    public void RemoveMember_Expel_NamesWhoExpelled() {
        Join();

        Assert.That(manager.RemoveMember(other.CharacterId, "Self"), Is.True);

        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyExpelMember("Self", "Other")) }));
    }

    [Test]
    public void RemoveMember_NotAMember_IsDropped() {
        Join();

        Assert.That(manager.RemoveMember(999), Is.False);

        Assert.That(guild.Members, Has.Count.EqualTo(2));
        Assert.That(Answers(), Is.Empty);
    }

    [Test]
    public void UpdateMemberRank_SetsTheRankAndNamesBoth() {
        Join();

        Assert.That(manager.UpdateMemberRank(self.CharacterId, other.CharacterId, 2), Is.True);

        Assert.That(other.Rank, Is.EqualTo(2));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyUpdateMemberRank("Self", "Other", 2)) }));
    }

    [Test]
    public void UpdateMemberRank_UnknownRequestorOrMember_IsDropped() {
        Join();

        Assert.That(manager.UpdateMemberRank(999, other.CharacterId, 2), Is.False);
        Assert.That(manager.UpdateMemberRank(self.CharacterId, 999, 2), Is.False);

        Assert.That(other.Rank, Is.EqualTo(1));
        Assert.That(Answers(), Is.Empty);
    }

    [Test]
    public void UpdateMemberMessage_StoresTheMessageAndSendsTheMember() {
        Join();

        Assert.That(manager.UpdateMemberMessage(other.CharacterId, "hello"), Is.True);

        Assert.That(other.Message, Is.EqualTo("hello"));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyUpdateMemberMessage(other)) }));
        Assert.That(manager.UpdateMemberMessage(999, "x"), Is.False);
    }

    [Test]
    public void WeeklyReset_ZeroesContributionAndRaidsAndTellsTheClient() {
        Join();
        other.WeeklyContribution = 40;
        other.TotalContribution = 90;
        other.Raids[3] = 12;

        manager.WeeklyReset();

        Assert.That(other.WeeklyContribution, Is.EqualTo(0));
        Assert.That(other.TotalContribution, Is.EqualTo(90));
        Assert.That(other.Raids, Is.Empty);
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.WeeklyContributionReset()) }));
    }

    [Test]
    public void UseBuff_ReplacesTheHeldSkillAndTellsTheClient() {
        Join();
        guild.Buffs.Add(new GuildBuff(5, 1, 0));
        var used = new GuildBuff(5, 1, 777);

        Assert.That(manager.UseBuff("Self", used, 0, 0, 0), Is.True);

        Assert.That(guild.Buffs[0], Is.EqualTo(used));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyUseBuff("Self", used)) }));
    }

    [Test]
    public void UseBuff_SkillTheGuildDoesNotHold_IsDropped() {
        Join();
        guild.Buffs.Add(new GuildBuff(5, 1, 0));

        Assert.That(manager.UseBuff("Self", new GuildBuff(6, 1, 1), 0, 0, 0), Is.False);

        Assert.That(guild.Buffs, Is.EqualTo(new[] { new GuildBuff(5, 1, 0) }));
        Assert.That(Answers(), Is.Empty);
    }

    [Test]
    public void UpgradeBuff_ReplacesTheLevel() {
        Join();
        guild.Buffs.Add(new GuildBuff(5, 1, 0));
        var upgraded = new GuildBuff(5, 2, 0);

        Assert.That(manager.UpgradeBuff("Self", upgraded), Is.True);
        Assert.That(manager.UpgradeBuff("Self", new GuildBuff(9, 1, 0)), Is.False);

        Assert.That(guild.Buffs, Is.EqualTo(new[] { upgraded }));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.UpgradeBuff("Self", upgraded)) }));
    }

    [Test]
    public void UpgradeNpc_AddsANewKindThenReplacesIt() {
        Join();
        var first = new GuildNpc(GuildNpcType.Goods, 1);
        var second = new GuildNpc(GuildNpcType.Goods, 2);

        Assert.That(manager.UpgradeNpc("Self", first), Is.True);
        Assert.That(manager.UpgradeNpc("Self", second), Is.True);

        Assert.That(guild.Npcs, Is.EqualTo(new[] { second }));
        Assert.That(Answers(), Is.EqualTo(new[] {
            Hex(GuildPacket.UpgradeNpc("Self", first)),
            Hex(GuildPacket.UpgradeNpc("Self", second)),
        }));
    }

    [Test]
    public void UpdateHouse_StoresRankAndTheme() {
        Join();

        Assert.That(manager.UpdateHouse("Self", 3, 4), Is.True);

        Assert.That(guild.HouseRank, Is.EqualTo(3));
        Assert.That(guild.HouseTheme, Is.EqualTo(4));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.UpgradeHouse("Self", 3, 4)) }));
    }

    [Test]
    public void UpdateNotice_StoresTheNotice() {
        Join();

        Assert.That(manager.UpdateNotice("Self", "news"), Is.True);

        Assert.That(guild.Notice, Is.EqualTo("news"));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyUpdateNotice("Self", 1, "news")) }));
    }

    [Test]
    public void UpdateRank_RenamesTheRankAndSetsItsPermission() {
        Join();

        Assert.That(manager.UpdateRank("Self", 1, "Elder", GuildPermission.All), Is.True);

        GuildRank rank = guild.Ranks.First(r => r.Id == 1);
        Assert.That(rank.Name, Is.EqualTo("Elder"));
        Assert.That(rank.Permission, Is.EqualTo(GuildPermission.All));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyUpdateRank(new InterfaceText("Self"), rank)) }));
    }

    // A rank renamed to the data's default name for its place shows the client's own default, as at load; any
    // other name is kept as typed.
    [Test]
    public void UpdateRank_TheDataDefaultName_ShowsTheClientDefault() {
        test.SetTable(new GuildRankNameTable(["C0", "C1", "C2", "C3", "C4", "C5"]));
        Join();

        Assert.That(manager.UpdateRank("Self", 1, GuildRules.DefaultRankNames(Target.LOCALE)[1], GuildPermission.All), Is.True);
        Assert.That(guild.Ranks.First(r => r.Id == 1).Name, Is.EqualTo("C1"));
        Assert.That(manager.UpdateRank("Self", 1, GuildRules.DefaultRankNames(Target.LOCALE)[0], GuildPermission.All), Is.True);
        Assert.That(guild.Ranks.First(r => r.Id == 1).Name, Is.EqualTo(GuildRules.DefaultRankNames(Target.LOCALE)[0]));
        Assert.That(manager.UpdateRank("Self", 1, "Elder", GuildPermission.All), Is.True);
        Assert.That(guild.Ranks.First(r => r.Id == 1).Name, Is.EqualTo("Elder"));
    }

    [Test]
    public void UpdateRank_UnknownRank_IsDropped() {
        Join();

        Assert.That(manager.UpdateRank("Self", 9, "Elder", GuildPermission.All), Is.False);

        Assert.That(Answers(), Is.Empty);
    }

    [Test]
    public void UpdateFocus_StoresListedAndFocus() {
        Join();

        Assert.That(manager.UpdateFocus("Self", true, GuildFocus.Social), Is.True);

        Assert.That(guild.Listed, Is.True);
        Assert.That(guild.Focus, Is.EqualTo(GuildFocus.Social));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyUpdateFocus("Self", true, GuildFocus.Social)) }));
    }

    [Test]
    public void UpdateEmblem_SendsTheEmblemThenTheNotice() {
        Join();

        Assert.That(manager.UpdateEmblem("Self", "emb"), Is.True);

        Assert.That(guild.Emblem, Is.EqualTo("emb"));
        Assert.That(Answers(), Is.EqualTo(new[] {
            Hex(GuildPacket.UpdateEmblem("emb")),
            Hex(GuildPacket.NotifyUpdateEmblem("Self", "emb")),
        }));
    }

    [Test]
    public void AddOrUpdatePoster_ReplacesThePosterOfTheSameId() {
        Join();
        var first = new GuildPoster { Id = 2, Picture = "a", OwnerName = "Self" };
        var second = new GuildPoster { Id = 2, Picture = "b", OwnerName = "Self" };

        Assert.That(manager.AddOrUpdatePoster(first), Is.True);
        Assert.That(manager.AddOrUpdatePoster(second), Is.True);

        Assert.That(guild.Posters, Is.EqualTo(new[] { second }));
        Assert.That(Answers(), Is.EqualTo(new[] {
            Hex(GuildPacket.UpdatePoster(first)),
            Hex(GuildPacket.UpdatePoster(second)),
        }));
    }

    [Test]
    public void UsePersonalBuff_WithoutAGuild_IsNullGuild() {
        Assert.That(manager.UsePersonalBuff(1), Is.EqualTo(GuildError.s_guild_err_null_guild));
        Assert.That(Answers(), Is.Empty);
    }
}

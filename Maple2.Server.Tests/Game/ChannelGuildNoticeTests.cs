using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util.Sync;
using Serilog;
using GuildManager = Maple2.Server.Game.Manager.GuildManager;
using GuildRequest = Maple2.Server.Channel.Service.GuildRequest;
using GuildResponse = Maple2.Server.Channel.Service.GuildResponse;

namespace Maple2.Server.Tests.Game;

// The World's guild notices to a member on this channel (ChannelService.Guild): each reaches the member's guild
// window as the client's packet for it, the member's copy of the guild changes with it, and a notice for a member
// who is not on this channel is dropped.
public class ChannelGuildNoticeTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long GuildId = 7;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private GuildManager manager = null!;
    private ChannelService channel = null!;
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
            new Dictionary<short, GuildTable.Property> {
                [1] = new GuildTable.Property(1, 0, 30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0f, 0f, 0, 0, 0, 0),
                [2] = new GuildTable.Property(2, 20, 40, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0f, 0f, 0, 0, 0, 0),
            }));
        test.Start();
        session = test.Session;
        manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        typeof(GuildManager).GetField("session", Any)!.SetValue(manager, session);
        typeof(GuildManager).GetField("tokenSource", Any)!.SetValue(manager, new CancellationTokenSource());
        typeof(GuildManager).GetField("logger", Any)!.SetValue(manager, Log.Logger);
        session.Guild = manager;

        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession> { [session.CharacterId] = session });
        channel = new ChannelService(server, new PlayerInfoStorage(test.World), test.Storage, null!, null!, null!);
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        manager?.Dispose();
        self?.Dispose();
        other?.Dispose();
    }

    private static GuildMember Member(long accountId, long characterId, string name, byte rank) => new() {
        GuildId = GuildId,
        Info = new PlayerInfo(new CharacterInfo(accountId, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []),
        Rank = rank,
        JoinTime = 1000,
    };

    private void Join() {
        self = Member(session.AccountId, session.CharacterId, "Self", 0);
        other = Member(session.AccountId + 1, session.CharacterId + 1, "Other", 1);
        guild = new Guild(GuildId, "Guild", self);
        guild.Members[self.CharacterId] = self;
        guild.Members[other.CharacterId] = other;
        guild.Ranks.Add(new GuildRank { Id = 0, Name = "Master", Permission = GuildPermission.All });
        guild.Ranks.Add(new GuildRank { Id = 1, Name = "Jr", Permission = (GuildPermission) 0 });
        typeof(GuildManager).GetProperty("Guild", Any)!.SetValue(manager, guild);
        session.Player.Value.Character.GuildId = GuildId;
    }

    private GuildError Notify(Action<GuildRequest> fill, long receiverId = 0) {
        var request = new GuildRequest { GuildId = GuildId, ReceiverIds = { receiverId == 0 ? session.CharacterId : receiverId } };
        fill(request);
        GuildResponse response = channel.Guild(request, null!).GetAwaiter().GetResult();
        return (GuildError) response.Error;
    }

    private string[] Sent(SendOp op = SendOp.Guild) => test.TakeSent().Where(packet => TestGameSession.Op(packet) == op).Select(Convert.ToHexString).ToArray();
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Invite_ShowsTheInviteToAMemberWithoutAGuild() {
        Assert.That(Notify(request => request.Invite = new GuildRequest.Types.Invite {
            GuildName = "Guild", SenderId = 900, SenderName = "Leader",
        }), Is.EqualTo(GuildError.none));

        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(GuildPacket.InviteInfo(new GuildInvite { GuildId = GuildId, GuildName = "Guild", SenderName = "Leader", ReceiverName = session.PlayerName })),
        }));
    }

    [Test]
    public void Invite_ToAMemberOfAGuild_IsRefused() {
        Join();

        Assert.That(Notify(request => request.Invite = new GuildRequest.Types.Invite { GuildName = "Other", SenderId = 900, SenderName = "Leader" }),
            Is.EqualTo(GuildError.s_guild_err_has_guild));
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void Invite_ToAPlayerNotOnThisChannel_IsNotFound() {
        Assert.That(Notify(request => request.Invite = new GuildRequest.Types.Invite { GuildName = "Guild", SenderId = 900, SenderName = "Leader" },
            session.CharacterId + 50), Is.EqualTo(GuildError.s_guild_err_null_user));
    }

    [Test]
    public void InviteReply_TellsTheInviterTheAnswer() {
        Notify(request => request.InviteReply = new GuildRequest.Types.InviteReply { Name = "Invitee", Reply = (int) GuildInvite.Response.RejectInvite });

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyInvite("Invitee", GuildInvite.Response.RejectInvite)) }));
    }

    [Test]
    public void AddMember_AddsTheNewMemberToTheList() {
        Join();
        long newId = session.CharacterId + 5;
        test.World.AddPlayer(session.AccountId + 5, newId, "Newcomer");

        Assert.That(Notify(request => request.AddMember = new GuildRequest.Types.AddMember {
            CharacterId = newId, RequestorName = "Self", Rank = 1, JoinTime = 2000,
        }), Is.EqualTo(GuildError.none));

        Assert.That(guild.Members.TryGetValue(newId, out GuildMember? added), Is.True);
        Assert.Multiple(() => {
            Assert.That(added!.Name, Is.EqualTo("Newcomer"));
            Assert.That(added.Rank, Is.EqualTo(1));
            Assert.That(added.JoinTime, Is.EqualTo(2000));
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(GuildPacket.Joined("Self", added)), Hex(GuildPacket.UpdateMember(added.Info)) }));
        });
        guild.Members.TryRemove(newId, out _);
        added!.Dispose();
    }

    [Test]
    public void AddMember_OfAnUnknownCharacter_FailsTheAdd() {
        Join();

        Assert.That(Notify(request => request.AddMember = new GuildRequest.Types.AddMember { CharacterId = 4040, RequestorName = "Self" }),
            Is.EqualTo(GuildError.s_guild_err_fail_addmember));
        Assert.That(guild.Members, Has.Count.EqualTo(2));
    }

    [Test]
    public void RemoveMember_OfAnother_TakesThemOffTheList() {
        Join();

        Notify(request => request.RemoveMember = new GuildRequest.Types.RemoveMember { CharacterId = other.CharacterId, RequestorName = "Self" });

        Assert.That(guild.Members.ContainsKey(other.CharacterId), Is.False);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyExpelMember("Self", "Other")) }));
    }

    [Test]
    public void RemoveMember_OfThisPlayer_ExpelsThemFromTheGuild() {
        Join();

        Notify(request => request.RemoveMember = new GuildRequest.Types.RemoveMember { CharacterId = session.CharacterId, RequestorName = "Master" });

        Assert.Multiple(() => {
            Assert.That(manager.Guild, Is.Null);
            Assert.That(session.Player.Value.Character.GuildId, Is.EqualTo(0));
            Assert.That(Sent(), Does.Contain(Hex(GuildPacket.NotifyExpel("Master"))));
        });
    }

    [Test]
    public void UpdateMember_RankMessageAndCheckIn() {
        Join();

        Notify(request => request.UpdateMember = new GuildRequest.Types.UpdateMember {
            RequestorId = self.CharacterId, CharacterId = other.CharacterId, Rank = 0, Message = "hi", Contribution = 10, CheckInTime = 5000,
        });

        Assert.Multiple(() => {
            Assert.That(other.Rank, Is.EqualTo(0));
            Assert.That(other.Message, Is.EqualTo("hi"));
            Assert.That(other.WeeklyContribution, Is.EqualTo(10));
            Assert.That(other.TotalContribution, Is.EqualTo(10));
            Assert.That(other.CheckinTime, Is.EqualTo(5000));
            Assert.That(Sent(), Is.EqualTo(new[] {
                Hex(GuildPacket.NotifyUpdateMemberRank("Self", "Other", 0)),
                Hex(GuildPacket.NotifyUpdateMemberMessage(other)),
                Hex(GuildPacket.GuildContribution(other, 10)),
                Hex(GuildPacket.CheckInTime("Other", 5000)),
            }));
        });
    }

    [Test]
    public void UpdateMember_WithNothingSet_SendsNothing() {
        Join();

        Notify(request => request.UpdateMember = new GuildRequest.Types.UpdateMember { RequestorId = self.CharacterId, CharacterId = other.CharacterId });

        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void UpdateContribution_SetsTheGuildsExpAndFunds() {
        Join();
        guild.Capacity = 30;

        Notify(request => request.UpdateContribution = new GuildRequest.Types.UpdateContribution {
            ContributorId = other.CharacterId, GuildExp = 30, GuildFund = 40,
        });

        Assert.Multiple(() => {
            Assert.That(guild.Experience, Is.EqualTo(30));
            Assert.That(guild.Funds, Is.EqualTo(40));
            Assert.That(guild.Capacity, Is.EqualTo(40));
            Assert.That(Sent(), Is.EqualTo(new[] {
                Hex(GuildPacket.GuildExperience(30)),
                Hex(GuildPacket.GuildFunds(40)),
                Hex(GuildPacket.NotifyUpdateCapacity(string.Empty, 40)),
            }));
        });
    }

    [Test]
    public void UpdateLeader_HandsTheGuildOver() {
        Join();

        Notify(request => request.UpdateLeader = new GuildRequest.Types.UpdateLeader { OldLeaderId = self.CharacterId, NewLeaderId = other.CharacterId });

        Assert.That(guild.LeaderCharacterId, Is.EqualTo(other.CharacterId));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyUpdateLeader("Self", "Other")) }));
    }

    [Test]
    public void UpdateNotice_EmblemRenameAndPoster() {
        Join();

        Notify(request => request.UpdateNotice = new GuildRequest.Types.UpdateNotice { RequestorName = "Self", Message = "notice" });
        Notify(request => request.UpdateEmblem = new GuildRequest.Types.UpdateEmblem { RequestorName = "Self", Emblem = "emblem" });
        Notify(request => request.Rename = new GuildRequest.Types.Rename { Name = "Renamed" });
        Notify(request => request.UpdatePoster = new GuildRequest.Types.UpdatePoster {
            Id = 2, Picture = "pic", OwnerId = other.CharacterId, OwnerName = "Other", ResourceId = 77,
        });

        GuildPoster poster = guild.Posters.Single();
        Assert.Multiple(() => {
            Assert.That(guild.Notice, Is.EqualTo("notice"));
            Assert.That(guild.Emblem, Is.EqualTo("emblem"));
            Assert.That(guild.Name, Is.EqualTo("Renamed"));
            Assert.That((poster.Id, poster.Picture, poster.OwnerId, poster.OwnerName, poster.ResourceId),
                Is.EqualTo((2, "pic", other.CharacterId, "Other", 77L)));
            Assert.That(Sent(), Is.EqualTo(new[] {
                Hex(GuildPacket.NotifyUpdateNotice("Self", 1, "notice")),
                Hex(GuildPacket.UpdateEmblem("emblem")),
                Hex(GuildPacket.NotifyUpdateEmblem("Self", "emblem")),
                Hex(GuildPacket.UpdateName("Renamed")),
                Hex(GuildPacket.UpdatePoster(poster)),
            }));
        });
    }

    [Test]
    public void UpdateRankAndFocus() {
        Join();

        Notify(request => request.UpdateRank = new GuildRequest.Types.UpdateRank {
            RequestorName = "Self", RankId = 1, Name = "Senior", Permission = (int) GuildPermission.All,
        });
        Notify(request => request.UpdateFocus = new GuildRequest.Types.UpdateFocus { RequestorName = "Self", Listed = false, Focus = (int) GuildFocus.Social });

        Assert.Multiple(() => {
            Assert.That(guild.Ranks[1].Name, Is.EqualTo("Senior"));
            Assert.That(guild.Ranks[1].Permission, Is.EqualTo(GuildPermission.All));
            Assert.That(guild.Listed, Is.False);
            Assert.That(guild.Focus, Is.EqualTo(GuildFocus.Social));
            Assert.That(Sent(), Is.EqualTo(new[] {
                Hex(GuildPacket.NotifyUpdateRank(new InterfaceText("Self"), guild.Ranks[1])),
                Hex(GuildPacket.NotifyUpdateFocus("Self", false, GuildFocus.Social)),
            }));
        });
    }

    [Test]
    public void Applications_AddedRemovedAndAnswered() {
        Join();
        long applicantId = session.CharacterId + 9;
        test.World.AddPlayer(session.AccountId + 9, applicantId, "Applicant");

        Notify(request => request.ApplicationAdded = new GuildRequest.Types.ApplicationAdded { ApplicationId = 31, ApplicantId = applicantId, CreationTime = 123 });
        string[] added = Sent();
        Notify(request => request.ApplicationRemoved = new GuildRequest.Types.ApplicationRemoved { ApplicationId = 31 });
        Notify(request => request.ApplicationAnswered = new GuildRequest.Types.ApplicationAnswered {
            ApplicationId = 32, RequestorName = "Self", ApplicantName = "Applicant", Accepted = true,
        });

        Assert.That(new PlayerInfoStorage(test.World).GetOrFetch(applicantId, out PlayerInfo? applicant), Is.True);
        Assert.That(added, Is.EqualTo(new[] {
            Hex(GuildPacket.ReceiveApplication(new GuildApplication { Id = 31, Guild = guild, Applicant = applicant!, CreationTime = 123 })),
        }));
        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(GuildPacket.WithdrawApplication(31)),
            Hex(GuildPacket.NotifyApplication("Self", "Applicant", 32, true)),
        }));
    }

    [Test]
    public void ApplicationAdded_OfAnUnknownApplicant_IsNotFound() {
        Join();

        Assert.That(Notify(request => request.ApplicationAdded = new GuildRequest.Types.ApplicationAdded { ApplicationId = 31, ApplicantId = 4040 }),
            Is.EqualTo(GuildError.s_guild_err_null_user));
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void ApplicationAdded_ToAPlayerWithoutAGuild_IsNotShown() {
        long applicantId = session.CharacterId + 9;
        test.World.AddPlayer(session.AccountId + 9, applicantId, "Applicant");

        Notify(request => request.ApplicationAdded = new GuildRequest.Types.ApplicationAdded { ApplicationId = 31, ApplicantId = applicantId });

        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void ApplicationResult_Declined_TellsTheApplicant() {
        Notify(request => request.ApplicationResult = new GuildRequest.Types.ApplicationResult { ApplicationId = 31, GuildName = "Guild", Joined = false });

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyApplicant("Guild", 31, false)) }));
        Assert.That(manager.Guild, Is.Null);
    }

    [Test]
    public void ApplicationResult_ToAMemberOfAGuild_IsDropped() {
        Join();

        Notify(request => request.ApplicationResult = new GuildRequest.Types.ApplicationResult { ApplicationId = 31, GuildName = "Guild", Joined = true });

        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void HouseEventRaidAndTrophy() {
        Join();
        guild.Events.Add(new GuildEvent(3, 0));

        Notify(request => request.UpdateHouse = new GuildRequest.Types.UpdateHouse { RequestorName = "Self", Rank = 2, Theme = 4 });
        Notify(request => request.OpenEvent = new GuildRequest.Types.OpenEvent { RequestorName = "Self", Id = 3, Time = 99 });
        Notify(request => request.MemberRaid = new GuildRequest.Types.MemberRaid { Name = "Other", RaidId = 5, Value = 6 });
        Notify(request => request.TrophyNotice = new GuildRequest.Types.TrophyNotice { Name = "Other", TrophyId = 11, Grade = 2, LastGrade = true });

        Assert.Multiple(() => {
            Assert.That((guild.HouseRank, guild.HouseTheme), Is.EqualTo((2, 4)));
            Assert.That(guild.Events.Single().Value, Is.EqualTo(99));
            Assert.That(other.Raids[5], Is.EqualTo(6));
            Assert.That(Sent(), Is.EqualTo(new[] {
                Hex(GuildPacket.UpgradeHouse("Self", 2, 4)),
                Hex(GuildPacket.NotifyOpenEvent("Self", new GuildEvent(3, 99))),
                Hex(GuildPacket.MemberRaid("Other", 5, 6)),
                Hex(GuildPacket.Achievement("Other", 11, 2, true)),
            }));
        });
    }

    [Test]
    public void UseBuff_StoresTheTimeOfUse() {
        Join();
        guild.Buffs.Add(new GuildBuff(4, 1, 0));

        Notify(request => request.UseBuff = new GuildRequest.Types.UseBuff { RequestorName = "Other", Id = 4, Level = 1, UseTime = 777 });
        // On the member's field thread, beside the handlers that read the skill list.
        Assert.That(guild.Buffs.Single(), Is.EqualTo(new GuildBuff(4, 1, 0)));
        test.RunFieldQueue();

        Assert.That(guild.Buffs.Single(), Is.EqualTo(new GuildBuff(4, 1, 777)));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(GuildPacket.NotifyUseBuff("Other", new GuildBuff(4, 1, 777))) }));
    }

    [Test]
    public void Recall_OffersTheRecallToTheOtherMembers() {
        Join();

        Notify(request => request.Recall = new GuildRequest.Types.Recall { CharacterId = other.CharacterId, Name = "Other", MapId = 2000001, EndTime = 900 });

        Assert.That(session.Recalls[(int) RecallScrollType.Guild], Is.EqualTo(new PendingRecall(other.CharacterId, "Other", 2000001, 900)));
        Assert.That(Sent(SendOp.RecallScroll), Is.EqualTo(new[] { Hex(RecallScrollPacket.Notify(RecallScrollType.Guild, "Other", 2000001, 900)) }));
    }

    [Test]
    public void Recall_IsNotOfferedToTheCallerNorOutOfTheGuild() {
        Join();

        Notify(request => request.Recall = new GuildRequest.Types.Recall { CharacterId = session.CharacterId, Name = "Self", MapId = 2000001, EndTime = 900 });
        Notify(request => {
            request.GuildId = GuildId + 1;
            request.Recall = new GuildRequest.Types.Recall { CharacterId = other.CharacterId, Name = "Other", MapId = 2000001, EndTime = 900 };
        });

        Assert.That(session.Recalls[(int) RecallScrollType.Guild], Is.Null);
        Assert.That(Sent(SendOp.RecallScroll), Is.Empty);
    }

    [Test]
    public void VsGameNotices_GoToTheGuildsMembers() {
        Join();

        Notify(request => request.VsGameTicket = new GuildRequest.Types.VsGameTicket { Registered = true, Notify = true });
        Notify(request => request.VsGameOpen = new GuildRequest.Types.VsGameOpen { GameId = 3 });
        Notify(request => request.VsGameCanceled = new GuildRequest.Types.VsGameCanceled());
        Notify(request => {
            request.GuildId = GuildId + 1;
            request.VsGameOpen = new GuildRequest.Types.VsGameOpen { GameId = 4 };
        });

        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(GuildPacket.VsGameTicket(true, true)),
            Hex(GuildPacket.VsGameOpen(3)),
            Hex(GuildPacket.VsGameMatchCanceled()),
        }));
    }

    [Test]
    public void UpdateStorage_SetsTheAmountLeft() {
        Join();

        Notify(request => request.UpdateStorage = new GuildRequest.Types.UpdateStorage { ItemId = 20000001, Rarity = 1, Amount = 3 });
        Notify(request => {
            request.GuildId = GuildId + 1;
            request.UpdateStorage = new GuildRequest.Types.UpdateStorage { ItemId = 20000002, Rarity = 1, Amount = 3 };
        });

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(GuildPacket.StorageItem(new RewardItem(20000001, 1, 3))) }));
    }

    [Test]
    public void PvpState_RegisteredUnregisteredAndMatched() {
        Join();

        Notify(request => request.PvpState = new GuildRequest.Types.PvpState { State = 1, Name = "Self", Line = 0x2b });
        Notify(request => request.PvpState = new GuildRequest.Types.PvpState { State = 0, Name = "Self", Line = 0x2c });
        Notify(request => request.PvpState = new GuildRequest.Types.PvpState { State = 2 });
        Notify(request => request.PvpMatched = new GuildRequest.Types.PvpMatched { Matched = true });

        Assert.That(guild.PvpState, Is.EqualTo((GuildPvpState) 2));
        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(GuildPacket.PvpRegistered("Self")),
            Hex(GuildPacket.PvpState((GuildPvpState) 1)),
            Hex(GuildPacket.PvpUnregistered("Self")),
            Hex(GuildPacket.PvpState((GuildPvpState) 0)),
            Hex(GuildPacket.PvpState((GuildPvpState) 2)),
            Hex(GuildPacket.PvpMatched(true)),
        }));
    }

    [Test]
    public void PvpRating_KeepsTheRecordAndTheMembersCounts() {
        Join();
        var pvp = new GuildPvp { SeasonId = guild.Pvp.SeasonId, Score = 1040, Wins = 3, Losses = 1 };

        Notify(request => request.PvpRating = new GuildRequest.Types.PvpRating {
            Pvp = JsonSerializer.Serialize(pvp), Won = true, Opponent = "Rival",
            Counts = { new GuildRequest.Types.PvpRating.Types.Count { CharacterId = other.CharacterId, Season = 2, Total = 5 } },
        });

        Assert.Multiple(() => {
            Assert.That((guild.Pvp.Score, guild.Pvp.Wins, guild.Pvp.Losses), Is.EqualTo((1040, 3, 1)));
            Assert.That((other.PvpSeasonCount, other.PvpTotalCount), Is.EqualTo((2, 5)));
            Assert.That(Sent(), Is.EqualTo(new[] {
                Hex(GuildPacket.PvpRating(1040, 3, 1)),
                Hex(GuildPacket.PvpResult(true, "Rival")),
                Hex(GuildPacket.PvpMemberCounts(new List<GuildMember> { other })),
            }));
        });
    }

    [Test]
    public void PvpNotice_WithoutAGuild_IsDropped() {
        Notify(request => request.PvpMatched = new GuildRequest.Types.PvpMatched { Matched = true });

        Assert.That(Sent(), Is.Empty);
    }

    // Each notice for a member who is not on this channel is dropped.
    [Test]
    public void NoticesForAPlayerNotOnThisChannel_AreDropped() {
        Join();
        long gone = session.CharacterId + 50;
        test.World.AddPlayer(session.AccountId + 5, session.CharacterId + 5, "Newcomer");
        Action<GuildRequest>[] notices = [
            request => request.InviteReply = new GuildRequest.Types.InviteReply { Name = "x" },
            request => request.AddMember = new GuildRequest.Types.AddMember { CharacterId = session.CharacterId + 5 },
            request => request.RemoveMember = new GuildRequest.Types.RemoveMember { CharacterId = other.CharacterId },
            request => request.UpdateMember = new GuildRequest.Types.UpdateMember { CharacterId = other.CharacterId, Rank = 0 },
            request => request.UpdateContribution = new GuildRequest.Types.UpdateContribution { GuildExp = 1 },
            request => request.UpdateLeader = new GuildRequest.Types.UpdateLeader { OldLeaderId = self.CharacterId, NewLeaderId = other.CharacterId },
            request => request.UpdateNotice = new GuildRequest.Types.UpdateNotice { Message = "x" },
            request => request.UpdateEmblem = new GuildRequest.Types.UpdateEmblem { Emblem = "x" },
            request => request.Rename = new GuildRequest.Types.Rename { Name = "x" },
            request => request.UpdatePoster = new GuildRequest.Types.UpdatePoster { Id = 1 },
            request => request.UpdateRank = new GuildRequest.Types.UpdateRank { RankId = 1, Name = "x" },
            request => request.UpdateFocus = new GuildRequest.Types.UpdateFocus { Listed = true },
            request => request.ApplicationRemoved = new GuildRequest.Types.ApplicationRemoved { ApplicationId = 1 },
            request => request.ApplicationAnswered = new GuildRequest.Types.ApplicationAnswered { ApplicationId = 1 },
            request => request.ApplicationResult = new GuildRequest.Types.ApplicationResult { ApplicationId = 1 },
            request => request.UpgradeNpc = new GuildRequest.Types.UpgradeNpc { Type = 1, Level = 1 },
            request => request.UpgradeBuff = new GuildRequest.Types.UpgradeBuff { Id = 1, Level = 1 },
            request => request.UpdateHouse = new GuildRequest.Types.UpdateHouse { Rank = 1 },
            request => request.UseBuff = new GuildRequest.Types.UseBuff { Id = 1 },
            request => request.OpenEvent = new GuildRequest.Types.OpenEvent { Id = 1 },
            request => request.Recall = new GuildRequest.Types.Recall { CharacterId = other.CharacterId },
            request => request.TrophyNotice = new GuildRequest.Types.TrophyNotice { Name = "x" },
            request => request.MemberRaid = new GuildRequest.Types.MemberRaid { Name = "Other", RaidId = 1 },
            request => request.VsGameCanceled = new GuildRequest.Types.VsGameCanceled(),
            request => request.UpdateStorage = new GuildRequest.Types.UpdateStorage { ItemId = 1 },
            request => request.PvpMatched = new GuildRequest.Types.PvpMatched(),
        ];

        foreach (Action<GuildRequest> notice in notices) {
            Assert.That(Notify(notice, gone), Is.EqualTo(GuildError.none));
        }

        Assert.Multiple(() => {
            Assert.That(guild.Members, Has.Count.EqualTo(2));
            Assert.That(guild.Notice, Is.Not.EqualTo("x"));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }
}

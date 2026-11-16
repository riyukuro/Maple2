using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Club;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using ClubRequest = Maple2.Server.Channel.Service.ClubRequest;
using ClubResponse = Maple2.Server.Channel.Service.ClubResponse;

namespace Maple2.Server.Tests.Game;

// The World's club notices to a member on this channel (ChannelService.Club): a new member, a new leader, a new
// name and a new buff each reach the member's club window as the client's packet for it, and the member's copy of
// the club changes with it. Then the club rows follow the other members' own updates (ClubManager.SyncUpdate).
public class ChannelClubNoticeTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long ClubId = 900;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private ChannelService channel = null!;
    private ClubManager manager = null!;
    private long otherId;
    private long newId;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        session = test.Session;
        test.SetTable(new ClubBuffTable(new Dictionary<int, ClubBuffTable.Entry>()));
        otherId = session.CharacterId + 500;
        newId = session.CharacterId + 600;
        test.World.AddPlayer(session.AccountId, session.CharacterId, session.PlayerName);
        test.World.AddPlayer(session.AccountId + 500, otherId, "other");
        test.World.AddPlayer(session.AccountId + 600, newId, "newcomer");
        manager = ClubManager.Create(new ClubInfo {
            Id = ClubId,
            Name = "club",
            LeaderId = session.CharacterId,
            LeaderName = session.PlayerName,
            State = (int) ClubState.Established,
            Members = {
                new ClubInfo.Types.Member { CharacterId = session.CharacterId, CharacterName = session.PlayerName },
                new ClubInfo.Types.Member { CharacterId = otherId, CharacterName = "other" },
            },
        }, session)!;
        Assume.That(manager, Is.Not.Null);
        session.Clubs ??= new ConcurrentDictionary<long, ClubManager>();
        session.Clubs[ClubId] = manager;

        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession> { [session.CharacterId] = session });
        channel = new ChannelService(server, session.PlayerInfo, test.Storage, null!, null!, null!);
        test.TakeSent();
    }

    private ClubError Notify(Action<ClubRequest> set, long clubId = ClubId, long receiverId = 0) {
        var request = new ClubRequest { ClubId = clubId, ReceiverIds = { receiverId == 0 ? session.CharacterId : receiverId } };
        set(request);
        ClubResponse response = channel.Club(request, null!).GetAwaiter().GetResult();
        return (ClubError) response.Error;
    }

    private string[] Sent() {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Club).Select(Convert.ToHexString).ToArray();
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter writer) => Convert.ToHexString(writer.ToArray());

    [Test]
    public void AddMember_TheNewcomerJoinsTheWindow() {
        Assert.That(Notify(request => request.AddMember = new ClubRequest.Types.AddMember {
            CharacterId = newId, RequestorName = "leader", JoinTime = 1234,
        }), Is.EqualTo(ClubError.none));

        Assert.That(manager.Club.Members.ContainsKey(newId), Is.True);
        ClubMember member = manager.Club.Members[newId];
        Assert.That(member.JoinTime, Is.EqualTo(1234));
        Assert.That(member.ClubId, Is.EqualTo(ClubId));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ClubPacket.NotifyAcceptInvite(member, "leader")) }));
    }

    // The newcomer's own updates reach the club window once it listens to them.
    [Test]
    public void AddMember_ThenTheNewcomersMapMove_IsTheMapRow() {
        Notify(request => request.AddMember = new ClubRequest.Types.AddMember { CharacterId = newId, RequestorName = "leader" });
        test.TakeSent();

        session.PlayerInfo.ReceiveUpdate(new PlayerUpdateRequest { AccountId = session.AccountId + 600, CharacterId = newId, MapId = 2000062 });

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ClubPacket.UpdateMemberMap(ClubId, "newcomer", 2000062)) }));
    }

    [Test]
    public void AddMember_AnUnknownCharacter_IsRefused() {
        Assert.That(Notify(request => request.AddMember = new ClubRequest.Types.AddMember { CharacterId = 77777, RequestorName = "leader" }),
            Is.EqualTo(ClubError.s_club_err_null_invite_member));
        Assert.That(manager.Club.Members.Count, Is.EqualTo(2));
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void UpdateLeader_TheMemberLeads() {
        Assert.That(Notify(request => request.UpdateLeader = new ClubRequest.Types.UpdateLeader { CharacterId = otherId }), Is.EqualTo(ClubError.none));

        Assert.That(manager.Club.Leader.CharacterId, Is.EqualTo(otherId));
        Assert.That(manager.Club.LeaderId, Is.EqualTo(otherId));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ClubPacket.UpdateLeader(ClubId, session.PlayerName, "other")) }));
    }

    [Test]
    public void UpdateLeader_NotAMember_IsTheUnknownError() {
        Notify(request => request.UpdateLeader = new ClubRequest.Types.UpdateLeader { CharacterId = 77777 });

        Assert.That(manager.Club.Leader.CharacterId, Is.EqualTo(session.CharacterId));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ClubPacket.Error(ClubError.s_club_err_unknown)) }));
    }

    [Test]
    public void Rename_TheClubTakesTheName() {
        Notify(request => request.Rename = new ClubRequest.Types.Rename { Name = "renamed", ChangedTime = 5555 });

        Assert.That(manager.Club.Name, Is.EqualTo("renamed"));
        Assert.That(manager.Club.NameChangeCooldown, Is.EqualTo(5555));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ClubPacket.Rename(ClubId, "renamed", 5555)) }));
    }

    [Test]
    public void UpdateBuff_TheClubTakesTheBuff() {
        Notify(request => request.UpdateBuff = new ClubRequest.Types.UpdateBuff { BuffId = 12, BuffLevel = 2 });

        Assert.That(manager.Club.BuffId, Is.EqualTo(12));
        Assert.That(manager.Club.BuffLevel, Is.EqualTo(2));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ClubPacket.ChangeBuff(ClubId, 12, 2)) }));
    }

    // A notice for a club this member does not hold, or for a member not on this channel, changes nothing.
    [Test]
    public void Notices_ForAnotherClubOrAbsentMember_AreDropped() {
        Notify(request => request.UpdateLeader = new ClubRequest.Types.UpdateLeader { CharacterId = otherId }, clubId: ClubId + 1);
        Notify(request => request.Rename = new ClubRequest.Types.Rename { Name = "renamed" }, clubId: ClubId + 1);
        Notify(request => request.UpdateBuff = new ClubRequest.Types.UpdateBuff { BuffId = 12, BuffLevel = 2 }, clubId: ClubId + 1);
        Notify(request => request.AddMember = new ClubRequest.Types.AddMember { CharacterId = newId }, clubId: ClubId + 1);
        Notify(request => request.UpdateLeader = new ClubRequest.Types.UpdateLeader { CharacterId = otherId }, receiverId: 424242);
        Notify(request => request.Rename = new ClubRequest.Types.Rename { Name = "renamed" }, receiverId: 424242);
        Notify(request => request.UpdateBuff = new ClubRequest.Types.UpdateBuff { BuffId = 12, BuffLevel = 2 }, receiverId: 424242);
        Notify(request => request.AddMember = new ClubRequest.Types.AddMember { CharacterId = newId }, receiverId: 424242);

        Assert.That(manager.Club.Leader.CharacterId, Is.EqualTo(session.CharacterId));
        Assert.That(manager.Club.Name, Is.EqualTo("club"));
        Assert.That(manager.Club.BuffId, Is.EqualTo(0));
        Assert.That(manager.Club.Members.ContainsKey(newId), Is.False);
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void Invite_ToAnAbsentMember_IsNullInviteMember() {
        Assert.That(Notify(request => request.Invite = new ClubRequest.Types.Invite { ClubName = "c", SenderId = otherId, SenderName = "other" },
            clubId: 901, receiverId: 424242), Is.EqualTo(ClubError.s_club_err_null_invite_member));
    }

    [Test]
    public void Invite_FromABlockedPlayer_IsRefused() {
        var blocked = (ConcurrentDictionary<long, Buddy>) typeof(BuddyManager).GetField("blocked", Any)!.GetValue(session.Buddy)!;
        blocked[1] = new Buddy(new BuddyEntry { Id = 1, OwnerId = session.CharacterId, BuddyId = 900, LastModified = 0, Type = BuddyType.Blocked },
            new PlayerInfo(new CharacterInfo(1900, 900, "Leader", "", "", Gender.Male, Job.Newbie, 20), "", new AchievementInfo(), []));

        Assert.That(Notify(request => request.Invite = new ClubRequest.Types.Invite { ClubName = "c", SenderId = 900, SenderName = "Leader" }, clubId: 901),
            Is.EqualTo(ClubError.s_club_err_blocked));
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void EmptyRequest_IsNone() {
        Assert.That(Notify(_ => { }), Is.EqualTo(ClubError.none));
        Assert.That(Sent(), Is.Empty);
    }

    #region Member updates
    private void Listen() {
        manager.Load();
        test.TakeSent();
    }

    private void Update(Action<PlayerUpdateRequest> set) {
        var request = new PlayerUpdateRequest { AccountId = session.AccountId + 500, CharacterId = otherId };
        set(request);
        session.PlayerInfo.ReceiveUpdate(request);
    }

    [Test]
    public void MemberLevelsUp_IsTheMemberRow() {
        Listen();
        Update(request => request.Level = 40);

        ClubMember member = manager.Club.Members[otherId];
        Assert.That(member.Info.Level, Is.EqualTo(40));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ClubPacket.UpdateMember(member)) }));
    }

    [Test]
    public void MemberRenamed_IsTheNameRowThenTheMemberRow() {
        Listen();
        Update(request => request.Name = "renamed");

        ClubMember member = manager.Club.Members[otherId];
        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(ClubPacket.UpdateMemberName(ClubId, "other", "renamed")),
            Hex(ClubPacket.UpdateMember(member)),
        }));
    }

    [Test]
    public void MemberLogsOutThenIn_IsTheNotice() {
        Listen();
        Update(request => {
            request.Channel = -1;
            request.LastOnlineTime = 4242;
        });
        ClubMember member = manager.Club.Members[otherId];
        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(ClubPacket.UpdateMember(member)),
            Hex(ClubPacket.NotifyLogout(ClubId, "other", 4242)),
        }));

        Update(request => request.Channel = 2);
        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(ClubPacket.UpdateMember(member)),
            Hex(ClubPacket.NotifyLogin(ClubId, "other")),
        }));
    }

    // The member's own row follows its updates, but it gets no login notice of itself.
    [Test]
    public void OwnLogin_IsNoNotice() {
        Listen();
        session.PlayerInfo.ReceiveUpdate(new PlayerUpdateRequest { AccountId = session.AccountId, CharacterId = session.CharacterId, Channel = -1 });

        Assert.That(Sent(), Has.Length.EqualTo(1));
    }

    // A member taken out stops its listener: its later updates send nothing.
    [Test]
    public void RemovedMember_IsNoLongerFollowed() {
        Listen();
        manager.RemoveMember(otherId);
        test.TakeSent();

        Update(request => request.Level = 40);

        Assert.That(Sent(), Is.Empty);
    }
    #endregion
}

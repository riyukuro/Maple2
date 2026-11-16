using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util.Sync;
using Maple2.Server.World.Service;
using PartyRequest = Maple2.Server.Channel.Service.PartyRequest;
using PartyResponse = Maple2.Server.Channel.Service.PartyResponse;

namespace Maple2.Server.Tests.Game;

// The World's party notices to a member on this channel (ChannelService.Party): each reaches the member's party
// window as the client's packet for it, the member's copy of the party changes with it, and a notice for another
// party or for a member who is not on this channel is dropped.
public class ChannelPartyNoticeTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int PartyId = 5;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private ChannelService channel = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        session = test.Session;
        session.Party = new PartyManager(test.World, session);

        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession> { [session.CharacterId] = session });
        channel = new ChannelService(server, new PlayerInfoStorage(test.World), test.Storage, null!, null!, null!);
        test.TakeSent();
    }

    private long OtherId => session.CharacterId + 1;
    private long ThirdId => session.CharacterId + 2;

    private PartyMember Member(long characterId, string name) => new() {
        PartyId = PartyId,
        Info = new PlayerInfo(new CharacterInfo(characterId + 1000, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []) { Channel = 1 },
    };

    // A party of the player and two others; the leader is the player or the second member.
    private Party Join(bool leaderIsSelf = true, int members = 3) {
        PartyMember me = Member(session.CharacterId, session.PlayerName);
        PartyMember other = Member(OtherId, "Other");
        var party = new Party(PartyId, leaderIsSelf ? me : other);
        party.Members[me.CharacterId] = me;
        party.Members[other.CharacterId] = other;
        if (members > 2) {
            PartyMember third = Member(ThirdId, "Third");
            party.Members[third.CharacterId] = third;
        }
        typeof(PartyManager).GetField("<Party>k__BackingField", Any)!.SetValue(session.Party, party);
        session.Player.Value.Character.PartyId = PartyId;
        return party;
    }

    private PartyError Notify(Action<PartyRequest> fill, long receiverId = 0, int partyId = PartyId) {
        var request = new PartyRequest { PartyId = partyId, ReceiverIds = { receiverId == 0 ? session.CharacterId : receiverId } };
        fill(request);
        PartyResponse response = channel.Party(request, null!).GetAwaiter().GetResult();
        return (PartyError) response.Error;
    }

    private string[] Sent(SendOp op = SendOp.Party) => test.TakeSent().Where(packet => TestGameSession.Op(packet) == op).Select(Convert.ToHexString).ToArray();
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Invite_ShowsTheInviteToAPlayerWithoutAParty() {
        Assert.That(Notify(request => request.Invite = new PartyRequest.Types.Invite { SenderId = 900, SenderName = "Leader" }, partyId: 9),
            Is.EqualTo(PartyError.none));

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.Invite(9, "Leader")) }));
    }

    [Test]
    public void Invite_DropsThePlayersPartyOfOne() {
        Join(members: 2);
        session.Party.Party!.Members.TryRemove(OtherId, out _);

        Notify(request => request.Invite = new PartyRequest.Types.Invite { SenderId = 900, SenderName = "Leader" }, partyId: 9);

        Assert.That(session.Party.Party, Is.Null);
        Assert.That(session.Player.Value.Character.PartyId, Is.Zero);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.Invite(9, "Leader")) }));
    }

    // An invite to the leader of a party of two or more becomes a request to join that party.
    [Test]
    public void Invite_ToAPartyLeader_AsksTheLeaderToTakeTheInviter() {
        Join();

        Assert.That(Notify(request => request.Invite = new PartyRequest.Types.Invite { SenderId = 900, SenderName = "Asker" }, partyId: 9),
            Is.EqualTo(PartyError.s_party_request_invite));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.JoinRequest("Asker")) }));
        Assert.That(session.Party.Id, Is.EqualTo(PartyId));
    }

    [Test]
    public void Invite_ToAMemberOfAParty_IsRefused() {
        Join(leaderIsSelf: false);

        Assert.That(Notify(request => request.Invite = new PartyRequest.Types.Invite { SenderId = 900, SenderName = "Asker" }, partyId: 9),
            Is.EqualTo(PartyError.s_party_err_cannot_invite));
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void Invite_FromABlockedPlayer_IsRefused() {
        var blocked = (ConcurrentDictionary<long, Buddy>) typeof(BuddyManager).GetField("blocked", Any)!.GetValue(session.Buddy)!;
        blocked[1] = new Buddy(new BuddyEntry { Id = 1, OwnerId = session.CharacterId, BuddyId = 900, LastModified = 0, Type = BuddyType.Blocked },
            new PlayerInfo(new CharacterInfo(1900, 900, "Leader", "", "", Gender.Male, Job.Newbie, 20), "", new AchievementInfo(), []));

        Assert.That(Notify(request => request.Invite = new PartyRequest.Types.Invite { SenderId = 900, SenderName = "Leader" }, partyId: 9),
            Is.EqualTo(PartyError.s_party_err_cannot_invite));
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void Invite_ToAPlayerNotOnThisChannel_IsAlreadyInvite() {
        Assert.That(Notify(request => request.Invite = new PartyRequest.Types.Invite { SenderId = 900, SenderName = "Leader" }, session.CharacterId + 50),
            Is.EqualTo(PartyError.s_party_err_alreadyInvite));
    }

    [Test]
    public void InviteReply_TellsTheInviterTheAnswer() {
        Notify(request => request.InviteReply = new PartyRequest.Types.InviteReply { Name = "Invitee", Reply = (int) PartyError.s_party_err_deny });

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.Error(PartyError.s_party_err_deny, "Invitee")) }));
    }

    [Test]
    public void AddMember_AddsTheNewMemberToTheList() {
        Join(members: 2);
        test.World.AddPlayer(ThirdId + 1000, ThirdId, "Third");

        Assert.That(Notify(request => request.AddMember = new PartyRequest.Types.AddMember { CharacterId = ThirdId, JoinTime = 1234 }),
            Is.EqualTo(PartyError.none));

        PartyMember added = session.Party.GetMember(ThirdId)!;
        Assert.That(added.Name, Is.EqualTo("Third"));
        Assert.That(added.JoinTime, Is.EqualTo(1234));
        Assert.That(session.Party.GetMember("Third"), Is.SameAs(added));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.Joined(added)) }));
    }

    [Test]
    public void AddMember_OfAnUnknownCharacter_CannotInvite() {
        Join(members: 2);

        Assert.That(Notify(request => request.AddMember = new PartyRequest.Types.AddMember { CharacterId = ThirdId }),
            Is.EqualTo(PartyError.s_party_err_cannot_invite));
        Assert.That(session.Party.GetMember(ThirdId), Is.Null);
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void RemoveMember_ALeave_TellsTheRestOfTheParty() {
        Join();

        Notify(request => request.RemoveMember = new PartyRequest.Types.RemoveMember { CharacterId = ThirdId });

        Assert.That(session.Party.GetMember(ThirdId), Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.Leave(ThirdId, false)) }));
    }

    [Test]
    public void RemoveMember_AKick_ShowsTheKickAndEndsTheVoteOnTheKickedMember() {
        Party party = Join();
        // Four voters need two yes votes; the initiator's own is one.
        Notify(request => request.StartVoteKick = new PartyRequest.Types.StartVoteKick {
            CharacterId = session.CharacterId, TargetId = ThirdId, ReceiverIds = { session.CharacterId, OtherId, ThirdId + 3, ThirdId + 4 }, VoteId = 3,
        });
        test.TakeSent();

        Notify(request => request.RemoveMember = new PartyRequest.Types.RemoveMember { CharacterId = ThirdId, IsKicked = true });

        Assert.That(party.Vote, Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(PartyPacket.PartyNotice(PartyMessage.s_party_vote_rejected_kick_user)),
            Hex(PartyPacket.EndVote()),
            Hex(PartyPacket.Kicked(ThirdId)),
        }));
    }

    [Test]
    public void RemoveMember_OfThePlayer_LeavesTheParty() {
        Join();

        Notify(request => request.RemoveMember = new PartyRequest.Types.RemoveMember { CharacterId = session.CharacterId });

        Assert.That(session.Party.Party, Is.Null);
        Assert.That(session.Player.Value.Character.PartyId, Is.Zero);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.Leave(session.CharacterId, true)) }));
    }

    // A matchmaker move took the player out of the party before the remove came.
    [Test]
    public void RemoveMember_ForAnotherParty_IsDropped() {
        Join();

        Notify(request => request.RemoveMember = new PartyRequest.Types.RemoveMember { CharacterId = ThirdId }, partyId: 9);

        Assert.That(session.Party.GetMember(ThirdId), Is.Not.Null);
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void UpdateLeader_MovesTheCrown() {
        Party party = Join();

        Notify(request => request.UpdateLeader = new PartyRequest.Types.UpdateLeader { CharacterId = OtherId });

        Assert.That(party.LeaderCharacterId, Is.EqualTo(OtherId));
        Assert.That(party.LeaderAccountId, Is.EqualTo(OtherId + 1000));
        Assert.That(party.LeaderName, Is.EqualTo("Other"));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.NotifyUpdateLeader(OtherId)) }));
    }

    [Test]
    public void UpdateLeader_ToANonMember_IsNotFound() {
        Party party = Join();

        Notify(request => request.UpdateLeader = new PartyRequest.Types.UpdateLeader { CharacterId = ThirdId + 9 });

        Assert.That(party.LeaderCharacterId, Is.EqualTo(session.CharacterId));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.Error(PartyError.s_party_err_not_found)) }));
    }

    [Test]
    public void Disband_ClosesThePartyWindow() {
        Join();

        Notify(request => request.Disband = new PartyRequest.Types.Disband { CharacterId = OtherId });

        Assert.That(session.Party.Party, Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.Disband()) }));
    }

    // A party of one goes quietly, unless World asks for the notice (a dungeon match party whose search ended).
    [Test]
    public void Disband_OfAPartyOfOne_IsToldOnlyWhenAsked() {
        Join(members: 2);
        session.Party.Party!.Members.TryRemove(OtherId, out _);
        Notify(request => request.Disband = new PartyRequest.Types.Disband());
        Assert.That(session.Party.Party, Is.Null);
        Assert.That(Sent(), Is.Empty);

        Join(members: 2);
        session.Party.Party!.Members.TryRemove(OtherId, out _);
        Notify(request => request.Disband = new PartyRequest.Types.Disband { Notify = true });
        Assert.That(session.Party.Party, Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.Disband()) }));
    }

    [Test]
    public void Disband_OfAnotherParty_IsDropped() {
        Join();

        Notify(request => request.Disband = new PartyRequest.Types.Disband(), partyId: 9);

        Assert.That(session.Party.Id, Is.EqualTo(PartyId));
        Assert.That(Sent(), Is.Empty);
    }

    // The matchmaker moved the player into another party: the old one goes without a packet and the new one loads.
    [Test]
    public void LoadParty_ReplacesThePartyWithTheOneWorldMovedThePlayerTo() {
        Join();
        test.World.AddPlayer(session.AccountId, session.CharacterId, session.PlayerName);
        test.World.AddPlayer(OtherId + 1000, OtherId, "Other");
        PartyInfoRequest? asked = null;
        test.World.PartyInfoWorld = request => {
            asked = request;
            return new PartyInfoResponse {
                Party = new PartyInfo {
                    Id = 9, LeaderCharacterId = OtherId, LeaderAccountId = OtherId + 1000, LeaderName = "Other", CreationTime = 50,
                    DungeonMatchKind = 2,
                    Members = { new PartyInfo.Types.Member { CharacterId = session.CharacterId }, new PartyInfo.Types.Member { CharacterId = OtherId } },
                },
            };
        };

        Notify(request => request.LoadParty = new PartyRequest.Types.LoadParty(), partyId: 9);

        Assert.That(asked!.PartyId, Is.EqualTo(9));
        Assert.That(session.Party.Id, Is.EqualTo(9));
        Assert.That(session.Party.Party!.Members.Keys, Is.EquivalentTo(new[] { session.CharacterId, OtherId }));
        Assert.That(session.Party.Party.DungeonMatchKind, Is.EqualTo(2));
        Assert.That(session.Player.Value.Character.PartyId, Is.EqualTo(9));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.Load(session.Party.Party, joinNotify: true)) }));
    }

    [Test]
    public void LoadParty_ThatWorldNoLongerHas_LeavesThePlayerWithoutAParty() {
        Join();

        Notify(request => request.LoadParty = new PartyRequest.Types.LoadParty(), partyId: 9);

        Assert.That(session.Party.Party, Is.Null);
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void ReadyCheck_StartsTheVoteForTheOnlineMembers() {
        Party party = Join();
        party.Members[ThirdId].Info.Channel = -1;

        Notify(request => request.StartReadyCheck = new PartyRequest.Types.StartReadyCheck { CharacterId = OtherId, VoteId = 4 });

        Assert.That(party.Vote!.Type, Is.EqualTo(PartyVoteType.ReadyCheck));
        Assert.That(party.Vote.Id, Is.EqualTo(4));
        Assert.That(party.Vote.Voters, Is.EquivalentTo(new[] { session.CharacterId, OtherId }));
        Assert.That(party.Vote.EndTime,
            Is.EqualTo(party.LastVoteTime + session.ServerTableMetadata.ConstantsTable.PartyVoteReadyDurationSeconds));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.StartVote(party.Vote)) }));
    }

    [Test]
    public void ReadyCheckReply_ShowsEachMembersFirstAnswer() {
        Party party = Join();
        Notify(request => request.StartReadyCheck = new PartyRequest.Types.StartReadyCheck { CharacterId = session.CharacterId, VoteId = 4 });
        test.TakeSent();

        PartyRequest.Types.VoteReply Reply(long characterId, bool ready) => new() { CharacterId = characterId, Reply = ready, PartyId = PartyId };
        Notify(request => request.VoteReply = Reply(OtherId, true));
        Notify(request => request.VoteReply = Reply(ThirdId, false));
        Notify(request => request.VoteReply = Reply(OtherId, false));
        Notify(request => request.VoteReply = Reply(ThirdId + 9, true));

        Assert.That(party.Vote!.Approvals, Is.EqualTo(new[] { session.CharacterId, OtherId }));
        Assert.That(party.Vote.Disapprovals, Is.EqualTo(new[] { ThirdId }));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.ReadyCheck(OtherId, true)), Hex(PartyPacket.ReadyCheck(ThirdId, false)) }));
    }

    [Test]
    public void EndVote_ClosesTheReadyCheck() {
        Party party = Join();
        Notify(request => request.StartReadyCheck = new PartyRequest.Types.StartReadyCheck { CharacterId = session.CharacterId, VoteId = 4 });
        test.TakeSent();

        Notify(request => request.EndVote = new PartyRequest.Types.EndVote { PartyId = PartyId });

        Assert.That(party.Vote, Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.EndVote()) }));
    }

    [Test]
    public void ExpiredVote_TellsTheVoteRanOut() {
        Party party = Join();
        Notify(request => request.StartReadyCheck = new PartyRequest.Types.StartReadyCheck { CharacterId = session.CharacterId, VoteId = 4 });
        test.TakeSent();

        Notify(request => request.ExpiredVote = new PartyRequest.Types.ExpiredVote { PartyId = PartyId });

        Assert.That(party.Vote, Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.PartyNotice(PartyMessage.s_party_vote_expired)), Hex(PartyPacket.EndVote()) }));
    }

    // A kick vote that got its votes ends without the rejected notice.
    [Test]
    public void EndVote_OfAPassedKickVote_HasNoRejectedNotice() {
        Party party = Join();
        Notify(request => request.StartVoteKick = new PartyRequest.Types.StartVoteKick {
            CharacterId = session.CharacterId, TargetId = ThirdId, ReceiverIds = { session.CharacterId, OtherId }, VoteId = 3,
        });
        Assert.That(party.Vote!.Type, Is.EqualTo(PartyVoteType.Kick));
        Assert.That(party.Vote.TargetMember!.CharacterId, Is.EqualTo(ThirdId));
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.StartVote(party.Vote)) }));
        for (int i = 0; party.Vote.Approvals.Count < party.Vote.VotesNeeded; i++) {
            party.Vote.Approvals.Add(session.CharacterId + i);
        }

        Notify(request => request.EndVote = new PartyRequest.Types.EndVote { PartyId = PartyId });

        Assert.That(party.Vote, Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.EndVote()) }));
    }

    [Test]
    public void StartVoteKick_OfANonMember_StartsNoVote() {
        Party party = Join();

        Notify(request => request.StartVoteKick = new PartyRequest.Types.StartVoteKick { CharacterId = session.CharacterId, TargetId = ThirdId + 9 });

        Assert.That(party.Vote, Is.Null);
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void VoteNotices_ForAnotherParty_AreDropped() {
        Party party = Join();
        Notify(request => request.StartReadyCheck = new PartyRequest.Types.StartReadyCheck { CharacterId = session.CharacterId, VoteId = 4 });
        test.TakeSent();

        Notify(request => request.VoteReply = new PartyRequest.Types.VoteReply { CharacterId = OtherId, Reply = true, PartyId = 9 });
        Notify(request => request.EndVote = new PartyRequest.Types.EndVote { PartyId = 9 });
        Notify(request => request.ExpiredVote = new PartyRequest.Types.ExpiredVote { PartyId = 9 });
        Notify(request => request.StartVoteKick = new PartyRequest.Types.StartVoteKick { CharacterId = OtherId, TargetId = ThirdId }, partyId: 9);

        Assert.That(party.Vote!.Type, Is.EqualTo(PartyVoteType.ReadyCheck));
        Assert.That(party.Vote.Approvals, Is.EqualTo(new[] { session.CharacterId }));
        Assert.That(Sent(), Is.Empty);
    }

    // The leader made a dungeon room: a member is asked whether to enter it.
    [Test]
    public void SetDungeon_ByTheLeader_AsksAMemberToEnter() {
        Party party = Join(leaderIsSelf: false);

        Notify(request => request.SetDungeon = new PartyRequest.Types.SetDungeon { PartyId = PartyId, DungeonId = 23050401, DungeonRoomId = 777, Set = true });

        Assert.That(party.DungeonId, Is.EqualTo(23050401));
        Assert.That(party.DungeonLobbyRoomId, Is.EqualTo(777));
        Assert.That(party.DungeonSet, Is.True);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.DungeonReset(true, 23050401)), Hex(PartyPacket.DungeonRoomCreate(23050401)) }));
    }

    [Test]
    public void SetDungeon_OfTheLeadersOwnRoom_OnlyResets() {
        Join();

        Notify(request => request.SetDungeon = new PartyRequest.Types.SetDungeon { PartyId = PartyId, DungeonId = 23050401, DungeonRoomId = 777, Set = true });

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.DungeonReset(true, 23050401)) }));
    }

    // The matchmaker made the room: every member, the leader too, gets the match box.
    [Test]
    public void SetDungeon_Matched_ShowsTheMatchBoxToEveryMember() {
        Join();

        Notify(request => request.SetDungeon = new PartyRequest.Types.SetDungeon {
            PartyId = PartyId, DungeonId = 23050401, DungeonRoomId = 777, Set = true, Matched = true,
        });

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.DungeonReset(true, 23050401)), Hex(PartyPacket.PartySearchDungeon()) }));
    }

    [Test]
    public void SetDungeon_Cleared_TellsTheRoomWasReset() {
        Party party = Join(leaderIsSelf: false);
        party.DungeonId = 23050401;

        Notify(request => request.SetDungeon = new PartyRequest.Types.SetDungeon { PartyId = PartyId });

        Assert.That(party.DungeonId, Is.Zero);
        Assert.That(party.DungeonSet, Is.False);
        Assert.That(Sent(), Is.EqualTo(new[] {
            Hex(PartyPacket.DungeonReset(false, 0)),
            Hex(PartyPacket.PartyNotice(PartyMessage.s_field_enteracne_party_notify_reset_dungeon, "Field_Enterance_Reset_Dungeon")),
        }));
    }

    [Test]
    public void SetDungeon_OfAnotherParty_IsDropped() {
        Party party = Join();

        Notify(request => request.SetDungeon = new PartyRequest.Types.SetDungeon { PartyId = 9, DungeonId = 23050401, Set = true });

        Assert.That(party.DungeonId, Is.Zero);
        Assert.That(Sent(), Is.Empty);
    }

    [Test]
    public void DungeonMatch_ShowsTheSearchAndItsEnd() {
        Party party = Join();

        Notify(request => request.DungeonMatch = new PartyRequest.Types.DungeonMatch { Kind = 2, Registered = true });
        Assert.That(party.DungeonMatchKind, Is.EqualTo(2));
        Notify(request => request.DungeonMatch = new PartyRequest.Types.DungeonMatch { Kind = 2, Registered = false });
        Assert.That(party.DungeonMatchKind, Is.Zero);
        Notify(request => request.DungeonMatch = new PartyRequest.Types.DungeonMatch { Kind = 2, Registered = true }, partyId: 9);

        Assert.That(party.DungeonMatchKind, Is.Zero);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.PartySearch(2, true)), Hex(PartyPacket.PartySearch(2, false)) }));
    }

    [Test]
    public void Recall_OffersTheRecallToTheOtherMembers() {
        Join();

        Notify(request => request.Recall = new PartyRequest.Types.Recall { CharacterId = OtherId, Name = "Other", MapId = 2000001, EndTime = 900 });

        Assert.That(session.Recalls[(int) RecallScrollType.Party], Is.EqualTo(new PendingRecall(OtherId, "Other", 2000001, 900)));
        Assert.That(Sent(SendOp.RecallScroll), Is.EqualTo(new[] { Hex(RecallScrollPacket.Notify(RecallScrollType.Party, "Other", 2000001, 900)) }));
    }

    // Leaving the party closes its recall, and so does the caller's leaving.
    [Test]
    public void Recall_ClosesWhenTheCallerLeaves() {
        Join();
        Notify(request => request.Recall = new PartyRequest.Types.Recall { CharacterId = OtherId, Name = "Other", MapId = 2000001, EndTime = 900 });
        test.TakeSent();

        Notify(request => request.RemoveMember = new PartyRequest.Types.RemoveMember { CharacterId = OtherId });

        Assert.That(session.Recalls[(int) RecallScrollType.Party], Is.Null);
        Assert.That(Sent(SendOp.RecallScroll), Is.EqualTo(new[] { Hex(RecallScrollPacket.Close(RecallScrollType.Party)) }));
    }

    [Test]
    public void Recall_IsNotOfferedToTheCallerNorOutOfTheParty() {
        Join();

        Notify(request => request.Recall = new PartyRequest.Types.Recall { CharacterId = session.CharacterId, Name = "Self", MapId = 2000001, EndTime = 900 });
        Notify(request => request.Recall = new PartyRequest.Types.Recall { CharacterId = OtherId, Name = "Other", MapId = 2000001, EndTime = 900 }, partyId: 9);

        Assert.That(session.Recalls[(int) RecallScrollType.Party], Is.Null);
        Assert.That(Sent(SendOp.RecallScroll), Is.Empty);
    }

    [Test]
    public void DungeonRecruit_ForMembers_ShowsTheMemberNotice() {
        Notify(request => request.DungeonRecruit = new PartyRequest.Types.DungeonRecruit {
            Kind = (int) DungeonRecruitKind.Member, LeaderName = "Leader", LeaderAccountId = 1900, LeaderId = 900, RoomId = 23050401,
            Members = 2, NeedHelpers = 1, NeedMembers = 2,
        });

        Assert.That(Sent(SendOp.DungeonHelp), Is.EqualTo(new[] { Hex(DungeonHelpPacket.Member(1, 2, PartyId, "Leader", 1900, 900, 23050401, 2)) }));
    }

    [Test]
    public void DungeonRecruit_ForHelpers_ShowsTheHelperNotice() {
        Notify(request => request.DungeonRecruit = new PartyRequest.Types.DungeonRecruit {
            Kind = (int) DungeonRecruitKind.Helper, LeaderName = "Leader", LeaderAccountId = 1900, LeaderId = 900, RoomId = 23050401, Members = 3,
        });

        Assert.That(Sent(SendOp.DungeonHelp), Is.EqualTo(new[] { Hex(DungeonHelpPacket.Helper(PartyId, "Leader", 1900, 900, 23050401, 3)) }));
    }

    [Test]
    public void JoinRequest_AsksTheLeader() {
        Notify(request => request.JoinRequest = new PartyRequest.Types.JoinRequest { Name = "Asker" });

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartyPacket.JoinRequest("Asker")) }));
    }

    [Test]
    public void ARequestWithNoNotice_IsNotFound() {
        Assert.That(Notify(_ => { }), Is.EqualTo(PartyError.s_party_err_not_found));
    }

    [Test]
    public void Notices_ForAMemberNotOnThisChannel_AreDropped() {
        Party party = Join();
        long away = session.CharacterId + 50;
        Action<PartyRequest>[] notices = [
            request => request.InviteReply = new PartyRequest.Types.InviteReply { Name = "Invitee" },
            request => request.RemoveMember = new PartyRequest.Types.RemoveMember { CharacterId = ThirdId },
            request => request.UpdateLeader = new PartyRequest.Types.UpdateLeader { CharacterId = OtherId },
            request => request.Disband = new PartyRequest.Types.Disband { Notify = true },
            request => request.LoadParty = new PartyRequest.Types.LoadParty(),
            request => request.StartReadyCheck = new PartyRequest.Types.StartReadyCheck { CharacterId = OtherId },
            request => request.VoteReply = new PartyRequest.Types.VoteReply { CharacterId = OtherId, PartyId = PartyId },
            request => request.EndVote = new PartyRequest.Types.EndVote { PartyId = PartyId },
            request => request.ExpiredVote = new PartyRequest.Types.ExpiredVote { PartyId = PartyId },
            request => request.StartVoteKick = new PartyRequest.Types.StartVoteKick { CharacterId = OtherId, TargetId = ThirdId },
            request => request.SetDungeon = new PartyRequest.Types.SetDungeon { PartyId = PartyId, DungeonId = 23050401, Set = true },
            request => request.DungeonMatch = new PartyRequest.Types.DungeonMatch { Kind = 2, Registered = true },
            request => request.Recall = new PartyRequest.Types.Recall { CharacterId = OtherId },
            request => request.DungeonRecruit = new PartyRequest.Types.DungeonRecruit { LeaderName = "Leader" },
            request => request.JoinRequest = new PartyRequest.Types.JoinRequest { Name = "Asker" },
        ];
        test.World.AddPlayer(ThirdId + 1000 + 5, ThirdId + 5, "Fourth");
        foreach (Action<PartyRequest> notice in notices) {
            Assert.That(Notify(notice, away), Is.EqualTo(PartyError.none));
        }
        Notify(request => request.AddMember = new PartyRequest.Types.AddMember { CharacterId = ThirdId + 5 }, away);

        Assert.That(party.Members.Count, Is.EqualTo(3));
        Assert.That(party.LeaderCharacterId, Is.EqualTo(session.CharacterId));
        Assert.That(party.Vote, Is.Null);
        Assert.That(session.Party.Party, Is.SameAs(party));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    #region PartySearch
    private PartySearchError Search(Action<PartySearchRequest> fill, long receiverId = 0, int partyId = PartyId) {
        var request = new PartySearchRequest { PartyId = partyId, ReceiverIds = { receiverId == 0 ? session.CharacterId : receiverId } };
        fill(request);
        return (PartySearchError) channel.PartySearch(request, null!).GetAwaiter().GetResult().Error;
    }

    private static PartySearchRequest.Types.Set Listing(long requestorId) => new() {
        RequestorId = requestorId,
        PartySearch = new PartySearchInfo {
            Id = 41, Name = "Chaos raid", Size = 4, LeaderAccountId = 1900, LeaderCharacterId = 900, LeaderName = "Leader",
            MemberCount = 3, NoApproval = true,
        },
    };

    // The party's listing reaches every member's party window; the member who listed it also gets the listing's add.
    [Test]
    public void PartySearchSet_TheListingReachesTheMember_AndTheRequestorGetsItsAdd() {
        Party party = Join();

        Assert.That(Search(request => request.Set = Listing(session.CharacterId)), Is.EqualTo(PartySearchError.none));

        Assert.That(party.Search, Is.Not.Null);
        Assert.That((party.Search!.Id, party.Search.Name, party.Search.Size, party.Search.PartyId, party.Search.LeaderCharacterId,
            party.Search.LeaderName, party.Search.MemberCount, party.Search.NoApproval), Is.EqualTo((41L, "Chaos raid", 4, PartyId, 900L,
            "Leader", 3, true)));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(PartyPacket.LoadPartySearchListing(party)), Hex(PartySearchPacket.Add(party.Search)),
        }));
    }

    [Test]
    public void PartySearchSet_AnotherMembersListing_NoAdd() {
        Party party = Join();

        Assert.That(Search(request => request.Set = Listing(OtherId)), Is.EqualTo(PartySearchError.none));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(PartyPacket.LoadPartySearchListing(party)) }));
    }

    [Test]
    public void PartySearchRemove_ClearsTheListing() {
        Party party = Join();
        Search(request => request.Set = Listing(OtherId));
        test.TakeSent();

        Assert.That(Search(request => request.Remove = new PartySearchRequest.Types.Remove()), Is.EqualTo(PartySearchError.none));

        Assert.That(party.Search, Is.Null);
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(PartyPacket.LoadPartySearchListing(party)) }));
    }

    // A member who is not on this channel, or whose party is another one, or an empty request: the World gets an error.
    [Test]
    public void PartySearch_AnotherPartyOrAnAbsentMember_IsRefused() {
        Party party = Join();

        Assert.That(Search(request => request.Set = Listing(OtherId), partyId: PartyId + 1), Is.EqualTo(PartySearchError.s_partysearch_err_server_db));
        Assert.That(Search(request => request.Set = Listing(OtherId), receiverId: 424242), Is.EqualTo(PartySearchError.s_partysearch_err_server_db));
        Assert.That(Search(request => request.Remove = new PartySearchRequest.Types.Remove(), partyId: PartyId + 1),
            Is.EqualTo(PartySearchError.s_partysearch_err_server_db));
        Assert.That(Search(request => request.Remove = new PartySearchRequest.Types.Remove(), receiverId: 424242),
            Is.EqualTo(PartySearchError.s_partysearch_err_server_db));
        Assert.That(Search(_ => { }), Is.EqualTo(PartySearchError.s_partysearch_err_server_db));

        Assert.That(party.Search, Is.Null);
        Assert.That(test.TakeSent(), Is.Empty);
    }
    #endregion
}

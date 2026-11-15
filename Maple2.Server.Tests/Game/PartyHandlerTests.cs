using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game;

// The party requests the client sends (Party, 0x0058 bytes): what the player is told when the request cannot go
// (the client's own party error texts) and what the world is asked when it can.
public class PartyHandlerTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private PartyHandler handler = null!;
    private readonly List<PartyRequest> asked = [];

    [SetUp]
    public void SetUp() {
        asked.Clear();
        test = new TestGameSession();
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata>()));
        test.Start();
        session.Party = new PartyManager(test.World, session);
        handler = new PartyHandler { World = test.World, ServerTableMetadata = test.ServerTableMetadata };
        test.World.PartyWorld = request => {
            asked.Add(request);
            return new PartyResponse();
        };
        test.TakeSent();
    }

    // The player stands in a party of members' count members; leaderIsSelf tells who leads.
    private Maple2.Model.Game.Party.Party Join(int members, bool leaderIsSelf, int partyId = 5) {
        PartyMember Member(long characterId) => new() {
            PartyId = partyId,
            Info = new PlayerInfo(new CharacterInfo(characterId + 1000, characterId, $"member{characterId}", "", "", Gender.Male, Job.Newbie, 20),
                "", new AchievementInfo(), []) { Channel = 1 },
        };
        PartyMember me = Member(session.CharacterId);
        PartyMember leader = leaderIsSelf ? me : Member(session.CharacterId + 1);
        var party = new Maple2.Model.Game.Party.Party(partyId, leader);
        party.Members[me.CharacterId] = me;
        party.Members[leader.CharacterId] = leader;
        for (int i = 2; party.Members.Count < members; i++) {
            PartyMember extra = Member(session.CharacterId + i);
            party.Members[extra.CharacterId] = extra;
        }
        typeof(PartyManager).GetField("<Party>k__BackingField", Any)!.SetValue(session.Party, party);
        return party;
    }

    private void Send(byte command, System.Action<Maple2.PacketLib.Tools.ByteWriter>? body = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            body?.Invoke(writer);
        }));
    }

    // The error codes of the Party packets sent since the last call (Party packet command 0 is the error).
    private List<PartyError> Errors() => test.TakeSent()
        .Where(packet => TestGameSession.Op(packet) == SendOp.Party && packet[2] == 0)
        .Select(packet => (PartyError) packet[3]).ToList();

    [Test]
    public void Invite_YourselfIsRefused() {
        Send(1, writer => writer.WriteUnicodeString(session.Player.Value.Character.Name));

        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_myself }));
        Assert.That(asked, Is.Empty);
    }

    [Test]
    public void Invite_OnlyTheLeaderMayInvite() {
        Join(2, leaderIsSelf: false);

        Send(1, writer => writer.WriteUnicodeString("someone"));

        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_not_chief }));
        Assert.That(asked, Is.Empty);
    }

    [Test]
    public void Invite_UnknownNameCannotBeInvited() {
        Join(2, leaderIsSelf: true);

        Send(1, writer => writer.WriteUnicodeString("nobody"));

        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_cannot_invite }));
        Assert.That(asked, Is.Empty);
    }

    [Test]
    public void Invite_WithoutAPartyAsksTheWorldToMakeOne() {
        test.World.PartyWorld = request => {
            asked.Add(request);
            return new PartyResponse { Error = (int) PartyError.s_party_err_already };
        };

        Send(1, writer => writer.WriteUnicodeString("nobody"));

        Assert.That(asked.Single().PartyCase, Is.EqualTo(PartyRequest.PartyOneofCase.Create));
        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_already }));
    }

    [Test]
    public void InviteResponse_InAPartyAlreadyIsRefused() {
        Join(2, leaderIsSelf: true);

        Send(2, writer => {
            writer.WriteUnicodeString("leader");
            writer.WriteByte(0);
            writer.WriteInt(9);
        });

        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_already }));
        Assert.That(asked, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InviteResponse_TheWorldGetsTheAnswerAndItsErrorIsShown(bool error) {
        test.World.PartyWorld = request => {
            asked.Add(request);
            return new PartyResponse { Error = error ? (int) PartyError.s_party_err_already : 0, Party = new PartyInfo { Id = 9 } };
        };

        Send(2, writer => {
            writer.WriteUnicodeString("leader");
            writer.WriteByte(1);
            writer.WriteInt(9);
        });

        PartyRequest.Types.RespondInvite respond = asked.Single().RespondInvite;
        Assert.That(respond.PartyId, Is.EqualTo(9));
        Assert.That(respond.Reply, Is.EqualTo(1));
        // A refusal is shown as is; an acceptance whose party has no leader to load is not found.
        Assert.That(Errors(), Is.EqualTo(new[] { error ? PartyError.s_party_err_already : PartyError.s_party_err_not_found }));
    }

    [TestCase((byte) 3)]
    [TestCase((byte) 17)]
    public void LeaveAndSetLeader_WithoutAPartyAreNotFound(byte command) {
        Send(command, writer => writer.WriteUnicodeString("x"));

        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_not_found }));
        Assert.That(asked, Is.Empty);
    }

    [Test]
    public void Kick_WithoutAPartyIsNotFound() {
        Send(4, writer => writer.WriteLong(77));

        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_not_found }));
    }

    [Test]
    public void Leave_AsksTheWorldAndShowsItsError() {
        Join(3, leaderIsSelf: false, partyId: 7);
        test.World.PartyWorld = request => {
            asked.Add(request);
            return new PartyResponse { Error = (int) PartyError.s_party_err_not_chief };
        };

        Send(3);

        Assert.That(asked.Single().Leave.PartyId, Is.EqualTo(7));
        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_not_chief }));
    }

    [Test]
    public void Kick_AsksTheWorldForTheTarget() {
        Join(3, leaderIsSelf: true, partyId: 7);

        Send(4, writer => writer.WriteLong(77));

        PartyRequest.Types.Kick kick = asked.Single().Kick;
        Assert.That(kick.PartyId, Is.EqualTo(7));
        Assert.That(kick.ReceiverId, Is.EqualTo(77));
        Assert.That(Errors(), Is.Empty);
    }

    [Test]
    public void SetLeader_UnknownNameIsNotExist() {
        Join(3, leaderIsSelf: true);

        Send(17, writer => writer.WriteUnicodeString("nobody"));

        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_not_exist }));
        Assert.That(asked, Is.Empty);
    }

    [Test]
    public void VoteKick_FeatureOffIsDropped() {
        test.SetFeaturesOff(PartyHandler.VoteKickFeature);
        Join(4, leaderIsSelf: true);

        Send(45, writer => writer.WriteLong(session.CharacterId + 2));

        Assert.That(Errors(), Is.Empty);
        Assert.That(asked, Is.Empty);
    }

    [Test]
    public void VoteKick_WithoutAPartyIsNotFound() {
        Send(45, writer => writer.WriteLong(77));

        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_not_found }));
    }

    [Test]
    public void VoteKick_YourselfOrAStrangerIsDropped() {
        Join(4, leaderIsSelf: true);

        Send(45, writer => writer.WriteLong(session.CharacterId));
        Send(45, writer => writer.WriteLong(99999));

        Assert.That(Errors(), Is.Empty);
        Assert.That(asked, Is.Empty);
    }

    [Test]
    public void VoteKick_NeedsFourMembers() {
        Join(3, leaderIsSelf: true);

        Send(45, writer => writer.WriteLong(session.CharacterId + 2));

        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_vote_need_more_people }));
        Assert.That(asked, Is.Empty);
    }

    [Test]
    public void VoteKick_OfAFullPartyAsksTheWorld() {
        Join(4, leaderIsSelf: true, partyId: 7);

        Send(45, writer => writer.WriteLong(session.CharacterId + 2));

        PartyRequest.Types.VoteKick vote = asked.Single().VoteKick;
        Assert.That(vote.PartyId, Is.EqualTo(7));
        Assert.That(vote.TargetUserId, Is.EqualTo(session.CharacterId + 2));
    }

    [Test]
    public void VoteKick_WhileAVoteRunsIsRefused() {
        Maple2.Model.Game.Party.Party party = Join(4, leaderIsSelf: true);
        party.LastVoteTime = System.DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        party.Vote = (PartyVote) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PartyVote));

        Send(45, writer => writer.WriteLong(session.CharacterId + 2));

        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_already_vote }));
        Assert.That(asked, Is.Empty);
    }

    [Test]
    public void ReadyCheck_WithoutAPartyOrAsAMemberIsRefused() {
        Send(46);
        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_not_found }));

        Join(2, leaderIsSelf: false);
        Send(46);
        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_not_chief }));
        Assert.That(asked, Is.Empty);
    }

    [Test]
    public void ReadyCheck_TheLeaderAsksTheWorld() {
        Join(2, leaderIsSelf: true, partyId: 7);

        Send(46);

        Assert.That(asked.Single().ReadyCheck.PartyId, Is.EqualTo(7));
        Assert.That(Errors(), Is.Empty);
    }

    [Test]
    public void VoteReply_WithoutAPartyIsNotFoundAndWithOneGoesToTheWorld() {
        Send(48, writer => {
            writer.WriteInt(3);
            writer.WriteBool(true);
        });
        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_not_found }));

        Join(2, leaderIsSelf: false, partyId: 7);
        Send(48, writer => {
            writer.WriteInt(3);
            writer.WriteBool(true);
        });

        PartyRequest.Types.VoteReply reply = asked.Single().VoteReply;
        Assert.That(reply.PartyId, Is.EqualTo(7));
        Assert.That(reply.VoteId, Is.EqualTo(3));
        Assert.That(reply.Reply, Is.True);
    }

    [Test]
    public void CancelPartySearch_NeedsAPartyAndSendsTheKind() {
        Send(34, writer => writer.WriteByte(2));
        Assert.That(asked, Is.Empty);

        Join(2, leaderIsSelf: false, partyId: 7);
        Send(34, writer => writer.WriteByte(2));

        PartyRequest.Types.CancelDungeonMatch cancel = asked.Single().CancelDungeonMatch;
        Assert.That(cancel.PartyId, Is.EqualTo(7));
        Assert.That(cancel.Kind, Is.EqualTo(2));
    }

    [TestCase((byte) 39)]
    [TestCase((byte) 43)]
    public void JoinDungeonRecruit_InAPartyAlreadyIsRefused(byte command) {
        Join(2, leaderIsSelf: true);

        Send(command, writer => {
            writer.WriteInt(9);
            writer.WriteInt(23050003);
        });

        Assert.That(Errors(), Is.EqualTo(new[] { PartyError.s_party_err_already }));
        Assert.That(asked, Is.Empty);
    }

    [Test]
    public void JoinDungeonRecruit_AsAMemberAsksTheWorldWithTheRoom() {
        test.World.PartyWorld = request => {
            asked.Add(request);
            return new PartyResponse { Party = new PartyInfo { Id = 9 } };
        };
        Send(43, writer => {
            writer.WriteInt(9);
            writer.WriteInt(23050003);
        });

        PartyRequest.Types.JoinDungeonRecruit join = asked.Single().JoinDungeonRecruit;
        Assert.That(join.PartyId, Is.EqualTo(9));
        Assert.That(join.RoomId, Is.EqualTo(23050003));
    }

    [Test]
    public void JoinDungeonRecruit_AsAHelperWithoutTheRewardIsDropped() {
        Send(39, writer => {
            writer.WriteInt(9);
            writer.WriteInt(23050003);
        });

        Assert.That(asked, Is.Empty);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void SummonParty_WithoutTheMeretsDoesNothing() {
        Join(2, leaderIsSelf: true);

        Send(29);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(asked, Is.Empty);
    }
}

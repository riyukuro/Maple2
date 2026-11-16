using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using ClubState = Maple2.Model.Enum.ClubState;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The club modes the client sends (CClubCli::SendClubMode1/6/8/10/12/13/14) with a World server that takes them:
// a party leader's create, an invite by name, an accepted invite that joins the club, and the checks the channel
// makes before it asks the World (a name the client would not send, a club the member does not hold, a member who
// does not lead the club).
public class ClubHandlerPathTests {
    private const long ClubId = 900;
    private const int BuffEffect = 110002001;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private ClubHandler handler = null!;
    private ClubManager manager = null!;
    private long otherId;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        session = test.Session;
        test.SetTable(new ClubBuffTable(new Dictionary<int, ClubBuffTable.Entry> { [1] = new(1, BuffEffect, 1) }));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { ClubNameLengthMin = 2, ClubNameLengthMax = 20 });
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        handler = new ClubHandler { World = test.World, BanWordStorage = new BanWordStorage(words), ServerTableMetadata = test.ServerTableMetadata };
        otherId = session.CharacterId + 500;
        test.World.AddPlayer(session.AccountId, session.CharacterId, session.PlayerName);
        test.World.AddPlayer(session.AccountId + 500, otherId, "other");
        manager = ClubManager.Create(Info(ClubId, "club", session.CharacterId), session)!;
        Assume.That(manager, Is.Not.Null);
        session.Clubs ??= new ConcurrentDictionary<long, ClubManager>();
        session.Clubs[ClubId] = manager;
        test.TakeSent();
    }

    private ClubInfo Info(long id, string name, long leaderId) {
        return new ClubInfo {
            Id = id,
            Name = name,
            LeaderId = leaderId,
            LeaderName = leaderId == otherId ? "other" : session.PlayerName,
            State = (int) ClubState.Established,
            Members = {
                new ClubInfo.Types.Member { CharacterId = session.CharacterId, CharacterName = session.PlayerName },
                new ClubInfo.Types.Member { CharacterId = otherId, CharacterName = "other" },
            },
        };
    }

    private void Send(byte mode, Action<IByteWriter> body) {
        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            body(writer);
        })));
    }

    private string[] ClubAnswers() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Club).Select(Convert.ToHexString).ToArray();
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static string Error(ClubError error) => Hex(ClubPacket.Error(error));

    private void LeadAParty() {
        var leader = new Maple2.Model.Game.Party.PartyMember {
            PartyId = 5,
            Info = new Maple2.Model.Game.PlayerInfo(new Maple2.Model.Game.CharacterInfo(session.AccountId, session.CharacterId, session.PlayerName, "", "",
                Maple2.Model.Enum.Gender.Male, Maple2.Model.Enum.Job.Newbie, 20), "", new Maple2.Model.Game.AchievementInfo(), []),
        };
        var party = new Maple2.Model.Game.Party.Party(5, session.AccountId, session.CharacterId, session.PlayerName);
        party.Members[leader.CharacterId] = leader;
        session.Party = new PartyManager(test.World, session);
        typeof(PartyManager).GetField("<Party>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(session.Party, party);
    }

    #region Create
    // A party leader's create goes to the World with the name; the staged club comes back as the World's notice.
    [Test]
    public void Create_ByThePartyLeader_AsksTheWorld() {
        LeadAParty();

        Send(1, writer => writer.WriteUnicodeString("newclub"));

        ClubRequest request = test.World.Clubs.Single();
        Assert.That(request.RequestorId, Is.EqualTo(session.CharacterId));
        Assert.That(request.Create.ClubName, Is.EqualTo("newclub"));
        Assert.That(ClubAnswers(), Is.Empty);
    }

    [Test]
    public void Create_RefusedByTheWorld_IsTheWorldsError() {
        LeadAParty();
        test.World.ClubError = (int) ClubError.s_club_err_name_exist;

        Send(1, writer => writer.WriteUnicodeString("newclub"));

        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_name_exist) }));
    }

    [Test]
    public void Create_WorldDown_IsUnknown() {
        LeadAParty();
        test.World.ClubWorld = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down"));

        Send(1, writer => writer.WriteUnicodeString("newclub"));

        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_unknown) }));
    }

    // A name the client checks itself (blank, out of the length range, a space) is refused before the World.
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("a")]
    [TestCase("abcdefghijklmnopqrstuvwxyz")]
    [TestCase("new club")]
    public void Create_ABadName_IsTheNameError(string name) {
        LeadAParty();

        Send(1, writer => writer.WriteUnicodeString(name));

        Assert.That(test.World.Clubs, Is.Empty);
        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_name_value) }));
    }
    #endregion

    #region Invite
    // The invite names a character: the World gets its id, and the inviter is told the invite went out.
    [Test]
    public void Invite_ACharacter_IsInvited() {
        Send(6, writer => {
            writer.WriteLong(ClubId);
            writer.WriteUnicodeString(session.PlayerName);
        });

        ClubRequest request = test.World.Clubs.Single();
        Assert.That(request.Invite.ClubId, Is.EqualTo(ClubId));
        Assert.That(request.Invite.ReceiverId, Is.EqualTo(session.CharacterId));
        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Hex(ClubPacket.Invited(ClubId, session.PlayerName)) }));
    }

    [Test]
    public void Invite_RefusedByTheWorld_IsTheWorldsError() {
        test.World.ClubError = (int) ClubError.s_club_err_full_club_member;

        Send(6, writer => {
            writer.WriteLong(ClubId);
            writer.WriteUnicodeString(session.PlayerName);
        });

        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_full_club_member) }));
    }

    [Test]
    public void Invite_NoSuchCharacter_IsNullInviteMember() {
        Send(6, writer => {
            writer.WriteLong(ClubId);
            writer.WriteUnicodeString("nobodyhere");
        });

        Assert.That(test.World.Clubs, Is.Empty);
        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_null_invite_member) }));
    }

    [Test]
    public void Invite_ToAClubNotHeld_IsNullClub() {
        Send(6, writer => {
            writer.WriteLong(ClubId + 1);
            writer.WriteUnicodeString(session.PlayerName);
        });

        Assert.That(test.World.Clubs, Is.Empty);
        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_null_club) }));
    }
    #endregion

    #region Invite reply
    private void Reply(long clubId, string invitee, bool accept) {
        Send(8, writer => {
            writer.WriteLong(clubId);
            writer.WriteUnicodeString("new club");
            writer.WriteUnicodeString("other");
            writer.WriteUnicodeString(invitee);
            writer.WriteBool(accept);
        });
    }

    // An accepted invite: mode 8 drops the listed invite, then the member joins (Join) and the club loads (Update).
    [Test]
    public void InviteReply_Accepted_JoinsTheClub() {
        const long newId = ClubId + 1;
        test.World.ClubWorld = _ => new Maple2.Server.World.Service.ClubResponse { Club = Info(newId, "new club", otherId) };

        Reply(newId, session.PlayerName, true);

        Assert.That(test.World.Clubs.Single().RespondInvite.Accept, Is.True);
        Assert.That(session.Clubs.ContainsKey(newId), Is.True);
        ClubManager joined = session.Clubs[newId];
        Assert.That(session.Player.Value.Character.ClubIds, Does.Contain(newId));
        Assert.That(ClubAnswers(), Is.EqualTo(new[] {
            Hex(ClubPacket.AcceptInvite(new Maple2.Model.Game.Club.ClubInvite { ClubId = newId, Name = "new club", LeaderName = "other", Invitee = session.PlayerName })),
            Hex(ClubPacket.Join(joined.Club.Members[session.CharacterId], "new club")),
            Hex(ClubPacket.Update(joined.Club)),
        }));
    }

    // A decline only drops the listed invite.
    [Test]
    public void InviteReply_Declined_OnlyDropsTheInvite() {
        Reply(ClubId + 1, session.PlayerName, false);

        Assert.That(test.World.Clubs.Single().RespondInvite.Accept, Is.False);
        Assert.That(session.Clubs.ContainsKey(ClubId + 1), Is.False);
        Assert.That(ClubAnswers(), Has.Length.EqualTo(1));
    }

    [Test]
    public void InviteReply_RefusedByTheWorld_DropsThenTheError() {
        test.World.ClubError = (int) ClubError.s_club_err_null_club;

        Reply(ClubId + 1, session.PlayerName, true);

        string[] sent = ClubAnswers();
        Assert.That(sent, Has.Length.EqualTo(2));
        Assert.That(sent[1], Is.EqualTo(Error(ClubError.s_club_err_null_club)));
        Assert.That(session.Clubs.ContainsKey(ClubId + 1), Is.False);
    }

    // A reply for another character's invite is not this member's to answer.
    [Test]
    public void InviteReply_ForAnotherInvitee_IsIgnored() {
        Reply(ClubId + 1, "someoneelse", true);

        Assert.That(test.World.Clubs, Is.Empty);
        Assert.That(ClubAnswers(), Is.Empty);
    }
    #endregion

    #region Leave, buff, leader, rename
    [Test]
    public void Leave_AClubNotHeld_IsNullClub() {
        Send(10, writer => writer.WriteLong(ClubId + 1));

        Assert.That(test.World.Clubs, Is.Empty);
        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_null_club) }));
    }

    [Test]
    public void Leave_AsksTheWorld() {
        Send(10, writer => writer.WriteLong(ClubId));

        Assert.That(test.World.Clubs.Single().Leave.ClubId, Is.EqualTo(ClubId));
        Assert.That(ClubAnswers(), Is.Empty);
    }

    private void Buff(long clubId, int buffId, int level) {
        Send(13, writer => {
            writer.WriteLong(clubId);
            writer.WriteInt(buffId);
            writer.WriteInt(level);
        });
    }

    [Test]
    public void Buff_ByTheLeader_AsksTheWorld() {
        Buff(ClubId, BuffEffect, 1);

        Assert.That(test.World.Clubs.Single().Buff.BuffId, Is.EqualTo(BuffEffect));
        Assert.That(ClubAnswers(), Is.Empty);
    }

    [Test]
    public void Buff_RefusedByTheWorld_IsTheWorldsError() {
        test.World.ClubError = (int) ClubError.s_club_err_unknown;
        Buff(ClubId, BuffEffect, 1);

        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_unknown) }));
    }

    [Test]
    public void Buff_NotTheLeader_IsNoMaster() {
        manager.Club.LeaderId = otherId;
        Buff(ClubId, BuffEffect, 1);

        Assert.That(test.World.Clubs, Is.Empty);
        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_no_master) }));
    }

    // A club not held, or a buff the club buff table does not list, is dropped with no answer.
    [Test]
    public void Buff_NotHeldOrNotListed_IsDropped() {
        Buff(ClubId + 1, BuffEffect, 1);
        Buff(ClubId, BuffEffect + 1, 1);

        Assert.That(test.World.Clubs, Is.Empty);
        Assert.That(ClubAnswers(), Is.Empty);
    }

    private void ChangeLeader(long clubId, string name) {
        Send(12, writer => {
            writer.WriteLong(clubId);
            writer.WriteUnicodeString(name);
        });
    }

    [Test]
    public void ChangeLeader_AsksTheWorld() {
        ChangeLeader(ClubId, "other");

        Assert.That(test.World.Clubs.Single().ChangeLeader.Name, Is.EqualTo("other"));
        Assert.That(ClubAnswers(), Is.Empty);
    }

    [Test]
    public void ChangeLeader_RefusedByTheWorld_IsTheWorldsError() {
        test.World.ClubError = (int) ClubError.s_club_err_no_master;
        ChangeLeader(ClubId, "other");

        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_no_master) }));
    }

    [Test]
    public void ChangeLeader_NoName_IsNullMember() {
        ChangeLeader(ClubId, "");

        Assert.That(test.World.Clubs, Is.Empty);
        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_null_member) }));
    }

    [Test]
    public void ChangeLeader_AClubNotHeld_IsDropped() {
        ChangeLeader(ClubId + 1, "other");

        Assert.That(test.World.Clubs, Is.Empty);
        Assert.That(ClubAnswers(), Is.Empty);
    }

    private void Rename(long clubId, string name) {
        Send(14, writer => {
            writer.WriteLong(clubId);
            writer.WriteUnicodeString(name);
        });
    }

    [Test]
    public void Rename_AsksTheWorld() {
        Rename(ClubId, "renamed");

        Assert.That(test.World.Clubs.Single().Rename.Name, Is.EqualTo("renamed"));
        Assert.That(ClubAnswers(), Is.Empty);
    }

    [Test]
    public void Rename_RefusedByTheWorld_IsTheWorldsError() {
        test.World.ClubError = (int) ClubError.s_club_err_name_exist;
        Rename(ClubId, "renamed");

        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_name_exist) }));
    }

    [TestCase("")]
    [TestCase("a")]
    [TestCase("abcdefghijklmnopqrstuvwxyz")]
    [TestCase("new name")]
    public void Rename_ABadName_IsTheNameError(string name) {
        Rename(ClubId, name);

        Assert.That(test.World.Clubs, Is.Empty);
        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Error(ClubError.s_club_err_name_value) }));
    }

    [Test]
    public void Rename_AClubNotHeld_IsDropped() {
        Rename(ClubId + 1, "renamed");

        Assert.That(test.World.Clubs, Is.Empty);
        Assert.That(ClubAnswers(), Is.Empty);
    }
    #endregion
}

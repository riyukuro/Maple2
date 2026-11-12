using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using ClubState = Maple2.Model.Enum.ClubState;
using Reply = Maple2.Model.Enum.ClubResponse;

namespace Maple2.Server.Tests.Game;

// Club 4, the answer to a member's own staged invite reply (client FUN_140b16a70: long club id, byte result; 0 drops
// the club, any other result is a club error it prints), and Club 0x1c, a member taken out without a notice
// (client FUN_140b14110: long club id, name), which a character deletion sends to the other members.
public class ClubMemberEventTests {
    private const long ClubId = 900;

    [Test]
    public void StagedInviteResult_IsIdThenByte() {
        var reader = new ByteReader(ClubPacket.StagedInviteResult(ClubId, ClubError.s_club_err_null_member).ToArray());
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Club));
            Assert.That(reader.ReadByte(), Is.EqualTo(4));
            Assert.That(reader.ReadLong(), Is.EqualTo(ClubId));
            Assert.That(reader.ReadByte(), Is.EqualTo((byte) ClubError.s_club_err_null_member));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }

    [Test]
    public void DeletedMember_IsIdThenName() {
        var reader = new ByteReader(ClubPacket.DeletedMember(ClubId, "gone").ToArray());
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Club));
            Assert.That(reader.ReadByte(), Is.EqualTo(0x1c));
            Assert.That(reader.ReadLong(), Is.EqualTo(ClubId));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("gone"));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }

    private static (TestGameSession, GameSession, ClubHandler) Start() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();
        var handler = new ClubHandler { World = test.World, BanWordStorage = null!, ServerTableMetadata = test.ServerTableMetadata };
        return (test, session, handler);
    }

    private static void SendReply(ClubHandler handler, GameSession session, Reply reply) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(ClubId);
            writer.WriteInt((int) reply);
        }));
    }

    private static string[] Sent(TestGameSession test) {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Club).Select(System.Convert.ToHexString).ToArray();
    }

    // A decline the world took gets result 0; an accept gets nothing (Club 1 follows once all have accepted).
    [TestCase(Reply.Reject, true)]
    [TestCase(Reply.Accept, false)]
    public void Reply_TheReplierGetsMode4OnADecline(Reply reply, bool answered) {
        (TestGameSession test, GameSession session, ClubHandler handler) = Start();

        SendReply(handler, session, reply);

        ClubRequest request = test.World.Clubs.Single();
        Assert.That(request.NewClubInvite.ClubId, Is.EqualTo(ClubId));
        Assert.That(request.NewClubInvite.Reply, Is.EqualTo((int) reply));
        string[] expected = answered ? [System.Convert.ToHexString(ClubPacket.StagedInviteResult(ClubId, ClubError.none).ToArray())] : [];
        Assert.That(Sent(test), Is.EqualTo(expected));
    }

    // A reply the world refuses gets the error code in mode 4, not the ErrorNotice (0x1d).
    [TestCase(Reply.Reject)]
    [TestCase(Reply.Accept)]
    public void RefusedReply_GetsTheErrorInMode4(Reply reply) {
        (TestGameSession test, GameSession session, ClubHandler handler) = Start();
        test.World.ClubError = (int) ClubError.s_club_err_null_club;

        SendReply(handler, session, reply);

        Assert.That(Sent(test), Is.EqualTo(new[] {
            System.Convert.ToHexString(ClubPacket.StagedInviteResult(ClubId, ClubError.s_club_err_null_club).ToArray()),
        }));
    }

    // A member who was deleted leaves the other members' club with 0x1c (no notice); a member who left with 0x12.
    [TestCase(true, 0x1c)]
    [TestCase(false, 0x12)]
    public void RemovedMember_DeletedGets0x1c(bool deleted, int mode) {
        (TestGameSession test, GameSession session, _) = Start();
        test.SetTable(new ClubBuffTable(new Dictionary<int, ClubBuffTable.Entry>()));
        long otherId = session.CharacterId + 500;
        test.World.AddPlayer(session.AccountId, session.CharacterId, session.PlayerName);
        test.World.AddPlayer(session.AccountId + 500, otherId, "other");
        ClubManager? manager = ClubManager.Create(new ClubInfo {
            Id = ClubId,
            Name = "club",
            LeaderId = session.CharacterId,
            LeaderName = session.PlayerName,
            State = (int) ClubState.Established,
            Members = {
                new ClubInfo.Types.Member { CharacterId = session.CharacterId, CharacterName = session.PlayerName },
                new ClubInfo.Types.Member { CharacterId = otherId, CharacterName = "other" },
            },
        }, session);
        Assume.That(manager, Is.Not.Null);
        session.Clubs ??= new ConcurrentDictionary<long, ClubManager>();
        session.Clubs[ClubId] = manager!;
        test.TakeSent();

        Assert.That(manager!.RemoveMember(otherId, deleted), Is.True);

        Assert.That(manager.Club.Members.Keys, Is.EqualTo(new[] { session.CharacterId }));
        string[] sent = Sent(test);
        Assert.That(sent, Has.Length.EqualTo(1));
        var reader = new ByteReader(System.Convert.FromHexString(sent[0]));
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Club));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        Assert.That(reader.ReadLong(), Is.EqualTo(ClubId));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("other"));
    }
}

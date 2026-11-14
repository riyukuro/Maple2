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
using Reply = Maple2.Model.Enum.ClubResponse;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The club modes the client sends (CClubCli::SendClubMode3/6/8/10/12/13/14) with a World server that cannot be reached.
// Before, every mode but the create let the RpcException out of the handler, and the session thread ended the
// session on it: the player was thrown out. Each mode now answers the client's unknown error (a staged reply in its
// own result, mode 4), and the invite answer still drops the listed invite (mode 8) first.
public class ClubModeHandlerTests {
    private const long ClubId = 900;
    private const int BuffEffect = 110002001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private ClubHandler handler = null!;
    private long otherId;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.SetTable(new ClubBuffTable(new Dictionary<int, ClubBuffTable.Entry> { [1] = new(1, BuffEffect, 1) }));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { ClubNameLengthMin = 2, ClubNameLengthMax = 20 });
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        handler = new ClubHandler { World = test.World, BanWordStorage = new BanWordStorage(words), ServerTableMetadata = test.ServerTableMetadata };
        otherId = session.CharacterId + 500;
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
        test.World.ClubWorld = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down"));
        test.TakeSent();
    }

    private void Send(byte mode, Action<IByteWriter> body) {
        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            body(writer);
        })));
    }

    private string[] ClubAnswers() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Club).Select(Convert.ToHexString).ToArray();
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static string[] Unknown => [Hex(ClubPacket.Error(ClubError.s_club_err_unknown))];

    [Test]
    public void StagedReply_WorldDown_UnknownInMode4() {
        Send(3, writer => {
            writer.WriteLong(ClubId);
            writer.WriteInt((int) Reply.Accept);
        });

        Assert.That(test.World.Clubs.Single().NewClubInvite.ClubId, Is.EqualTo(ClubId));
        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Hex(ClubPacket.StagedInviteResult(ClubId, ClubError.s_club_err_unknown)) }));
    }

    [Test]
    public void Invite_WorldDown_Unknown() {
        Send(6, writer => {
            writer.WriteLong(ClubId);
            writer.WriteUnicodeString(session.PlayerName);
        });

        Assert.That(test.World.Clubs.Single().Invite.ClubId, Is.EqualTo(ClubId));
        Assert.That(ClubAnswers(), Is.EqualTo(Unknown));
    }

    // The invite reply: mode 8 drops the listed invite (kept, the next invite to the club stops the client), then
    // the error.
    [Test]
    public void InviteReply_WorldDown_DroppedThenUnknown() {
        var invite = new Maple2.Model.Game.Club.ClubInvite { ClubId = 901, Name = "other club", LeaderName = "leader", Invitee = session.PlayerName };
        Send(8, writer => {
            writer.WriteLong(invite.ClubId);
            writer.WriteUnicodeString(invite.Name);
            writer.WriteUnicodeString(invite.LeaderName);
            writer.WriteUnicodeString(invite.Invitee);
            writer.WriteBool(true);
        });

        Assert.That(test.World.Clubs.Single().RespondInvite.ClubId, Is.EqualTo(901));
        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Hex(ClubPacket.AcceptInvite(invite)), Unknown[0] }));
        Assert.That(session.Clubs.ContainsKey(901), Is.False);
    }

    [Test]
    public void Leave_WorldDown_Unknown() {
        Send(10, writer => writer.WriteLong(ClubId));

        Assert.That(test.World.Clubs.Single().Leave.ClubId, Is.EqualTo(ClubId));
        Assert.That(ClubAnswers(), Is.EqualTo(Unknown));
        Assert.That(session.Clubs.ContainsKey(ClubId), Is.True);
    }

    [Test]
    public void ChangeLeader_WorldDown_Unknown() {
        Send(12, writer => {
            writer.WriteLong(ClubId);
            writer.WriteUnicodeString("other");
        });

        Assert.That(test.World.Clubs.Single().ChangeLeader.Name, Is.EqualTo("other"));
        Assert.That(ClubAnswers(), Is.EqualTo(Unknown));
    }

    [Test]
    public void Buff_WorldDown_Unknown() {
        Send(13, writer => {
            writer.WriteLong(ClubId);
            writer.WriteInt(BuffEffect);
            writer.WriteInt(1);
        });

        Assert.That(test.World.Clubs.Single().Buff.BuffId, Is.EqualTo(BuffEffect));
        Assert.That(ClubAnswers(), Is.EqualTo(Unknown));
    }

    [Test]
    public void Rename_WorldDown_Unknown() {
        Send(14, writer => {
            writer.WriteLong(ClubId);
            writer.WriteUnicodeString("newname");
        });

        Assert.That(test.World.Clubs.Single().Rename.Name, Is.EqualTo("newname"));
        Assert.That(ClubAnswers(), Is.EqualTo(Unknown));
    }

    // The World's own answer still comes through as before.
    [Test]
    public void Leave_Refused_WorldError() {
        test.World.ClubWorld = _ => new Maple2.Server.World.Service.ClubResponse { Error = (int) ClubError.s_club_err_null_member };
        Send(10, writer => writer.WriteLong(ClubId));

        Assert.That(ClubAnswers(), Is.EqualTo(new[] { Hex(ClubPacket.Error(ClubError.s_club_err_null_member)) }));
    }
}

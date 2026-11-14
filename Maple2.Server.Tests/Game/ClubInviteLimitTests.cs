using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using ClubRequest = Maple2.Server.Channel.Service.ClubRequest;
using ClubResponse = Maple2.Server.Channel.Service.ClubResponse;
using GameClubManager = Maple2.Server.Game.Manager.ClubManager;

namespace Maple2.Server.Tests.Game;

// The client lists each club invite (Club 7) until Club 8 answers its reply, counts the listed invites with its
// clubs, and drops with no box an invite past 3 or a second one of a club it lists (CClubClientManager::
// IsClubLimitReached and FUN_140b16d20, clubcli.cpp:577-591 and 1097-1111). The channel counted the clubs alone, so
// the inviter was told the invite went out and the invitee never saw it.
public class ClubInviteLimitTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private ChannelService channel = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        session.Clubs ??= new ConcurrentDictionary<long, GameClubManager>();
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession> { [session.CharacterId] = session });
        channel = new ChannelService(server, null!, test.Storage, null!, null!, null!);
        test.TakeSent();
    }

    private ClubError Invite(long clubId) {
        ClubResponse response = channel.Club(new ClubRequest {
            ClubId = clubId,
            ReceiverIds = { session.CharacterId },
            Invite = new ClubRequest.Types.Invite {
                ClubName = $"club{clubId}",
                SenderId = session.CharacterId + 500,
                SenderName = "leader",
            },
        }, null!).GetAwaiter().GetResult();
        return (ClubError) response.Error;
    }

    // The Club 7 invites sent, by club id.
    private long[] InvitesSent() {
        return test.TakeSent()
            .Where(packet => TestGameSession.Op(packet) == SendOp.Club && packet[2] == 7)
            .Select(packet => new ByteReader(packet, 3).ReadLong())
            .ToArray();
    }

    // Only the number of clubs is read.
    private void AddClub(long clubId) {
        session.Clubs[clubId] = (GameClubManager) RuntimeHelpers.GetUninitializedObject(typeof(GameClubManager));
    }

    [Test]
    public void ThreeOpenInvites_TheFourthIsRefused() {
        Assert.That(Invite(1), Is.EqualTo(ClubError.none));
        Assert.That(Invite(2), Is.EqualTo(ClubError.none));
        Assert.That(Invite(3), Is.EqualTo(ClubError.none));
        Assert.That(Invite(4), Is.EqualTo(ClubError.s_club_err_full_club_member));
        Assert.That(InvitesSent(), Is.EqualTo(new long[] { 1, 2, 3 }));
    }

    [Test]
    public void TwoClubsAndAnOpenInvite_TheNextIsRefused() {
        AddClub(10);
        AddClub(11);
        Assert.That(Invite(1), Is.EqualTo(ClubError.none));
        Assert.That(Invite(2), Is.EqualTo(ClubError.s_club_err_full_club_member));
        Assert.That(InvitesSent(), Is.EqualTo(new long[] { 1 }));
    }

    // A second invite of a listed club is not sent again (the client would drop it); the open box answers it.
    [Test]
    public void RepeatInvite_NotSentAgain() {
        Assert.That(Invite(1), Is.EqualTo(ClubError.none));
        Assert.That(Invite(1), Is.EqualTo(ClubError.none));
        Assert.That(InvitesSent(), Is.EqualTo(new long[] { 1 }));
    }

    // The reply (c2s mode 8) is answered with Club 8, which drops the invite from the client's list: the slot is free
    // again, even when the World cannot be reached.
    [Test]
    public void Reply_FreesTheSlot() {
        Invite(1);
        Invite(2);
        Invite(3);
        Assert.That(Invite(4), Is.EqualTo(ClubError.s_club_err_full_club_member));
        InvitesSent();

        test.World.ClubWorld = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down"));
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var handler = new ClubHandler { World = test.World, BanWordStorage = new BanWordStorage(words), ServerTableMetadata = test.ServerTableMetadata };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(8);
            writer.WriteLong(2);
            writer.WriteUnicodeString("club2");
            writer.WriteUnicodeString("leader");
            writer.WriteUnicodeString(session.PlayerName);
            writer.WriteBool(false);
        }));
        test.TakeSent();

        Assert.That(Invite(4), Is.EqualTo(ClubError.none));
        Assert.That(InvitesSent(), Is.EqualTo(new long[] { 4 }));
    }

    [Test]
    public void List_EntryExpiresAfterTheWorldInviteTime() {
        var list = new ClubInviteList();
        list.Add(1, 1000);
        Assert.That(list.Contains(1, 1000 + ClubInviteList.Lifetime - 1), Is.True);
        Assert.That(list.Count(1000 + ClubInviteList.Lifetime - 1), Is.EqualTo(1));
        Assert.That(list.Contains(1, 1000 + ClubInviteList.Lifetime), Is.False);
        Assert.That(list.Count(1000 + ClubInviteList.Lifetime), Is.EqualTo(0));
    }
}

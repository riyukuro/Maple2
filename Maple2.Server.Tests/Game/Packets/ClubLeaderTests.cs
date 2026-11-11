using System;
using Maple2.Model.Error;
using Maple2.Model.Game.Club;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class ClubLeaderTests {
    private static readonly (long, string)[] Members = [(1, "Lead"), (2, "Mate"), (3, "Other")];

    [Test]
    public void OnlyTheLeaderHandsOver() {
        Assert.That(ClubLeaderRule.Pick(1, 2, Members, "Other", out long id), Is.EqualTo(ClubError.s_club_err_no_master));
        Assert.That(id, Is.EqualTo(0));
    }

    [Test]
    public void TargetIsAMemberAndNotTheLeader() {
        Assert.That(ClubLeaderRule.Pick(1, 1, Members, "mate", out long id), Is.EqualTo(ClubError.none));
        Assert.That(id, Is.EqualTo(2));
        Assert.That(ClubLeaderRule.Pick(1, 1, Members, "Nobody", out _), Is.EqualTo(ClubError.s_club_err_null_member));
        Assert.That(ClubLeaderRule.Pick(1, 1, Members, "Lead", out _), Is.EqualTo(ClubError.s_club_err_unknown));
    }

    [Test]
    public void UpdateLeaderBytes() {
        ByteWriter writer = ClubPacket.UpdateLeader(0x0102030405060708, "a", "b");
        byte[] body = writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();
        // sub-type 21, club id, old leader, new leader, show the notice
        Assert.That(body, Is.EqualTo(new byte[] { 21, 8, 7, 6, 5, 4, 3, 2, 1, 1, 0, (byte) 'a', 0, 1, 0, (byte) 'b', 0, 1 }));
    }
}

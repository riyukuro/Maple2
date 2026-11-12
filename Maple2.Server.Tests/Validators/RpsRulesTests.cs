using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

// Microgame 0x0114 (rock-paper-scissors). Made-up ids and item ids.
public class RpsRulesTests {
    [TestCase(RpsRules.Rock, RpsRules.Rock, RpsRules.Draw)]
    [TestCase(RpsRules.Paper, RpsRules.Rock, RpsRules.Win)]
    [TestCase(RpsRules.Scissors, RpsRules.Paper, RpsRules.Win)]
    [TestCase(RpsRules.Rock, RpsRules.Scissors, RpsRules.Win)]
    [TestCase(RpsRules.Rock, RpsRules.Paper, RpsRules.Lose)]
    [TestCase(RpsRules.Paper, RpsRules.Scissors, RpsRules.Lose)]
    [TestCase(RpsRules.Scissors, RpsRules.Rock, RpsRules.Lose)]
    public void Result_FollowsTheCards(int mine, int theirs, byte expected) {
        Assert.That(RpsRules.Result(mine, theirs), Is.EqualTo(expected));
    }

    [Test]
    public void ResultCondition_MapsToPlayRps() {
        Assert.Multiple(() => {
            Assert.That(RpsRules.ResultCondition(RpsRules.Win), Is.EqualTo(ConditionType.play_rps_win));
            Assert.That(RpsRules.ResultCondition(RpsRules.Lose), Is.EqualTo(ConditionType.play_rps_lose));
            Assert.That(RpsRules.ResultCondition(RpsRules.Draw), Is.EqualTo(ConditionType.play_rps_draw));
        });
    }

    [TestCase(-1, false)]
    [TestCase(0, true)]
    [TestCase(2, true)]
    [TestCase(3, false)]
    public void IsCard_OnlyThree(int card, bool expected) {
        Assert.That(RpsRules.IsCard(card), Is.EqualTo(expected));
    }

    [Test]
    public void Claimed_IsACommaList() {
        Assert.That(RpsRules.ParseClaimed(null), Is.Empty);
        Assert.That(RpsRules.ParseClaimed(""), Is.Empty);
        Assert.That(RpsRules.ParseClaimed("0, 2,x"), Is.EqualTo(new[] { 0, 2 }));
        Assert.That(RpsRules.AddClaimed("", 1), Is.EqualTo("1"));
        Assert.That(RpsRules.AddClaimed("0,1", 3), Is.EqualTo("0,1,3"));
    }

    [TestCase(0, 5, "", true)]
    [TestCase(1, 5, "", false)]   // needs 10 plays
    [TestCase(1, 10, "0", true)]
    [TestCase(0, 10, "0", false)] // claimed already
    [TestCase(-1, 10, "", false)]
    [TestCase(2, 99, "", false)]  // no such reward
    public void CanClaim_PlayCountAndOnce(int index, int playCount, string claimed, bool expected) {
        Assert.That(RpsRules.CanClaim(index, [5, 10], playCount, claimed), Is.EqualTo(expected));
    }

    [Test]
    public void Table_InviteStartPlay() {
        var table = new RpsMatchTable();
        Assert.That(table.Invite(1, 1, 0), Is.False);
        Assert.That(table.Invite(1, 2, 0), Is.True);
        Assert.That(table.Invite(3, 2, 0), Is.False); // the invitee is busy
        Assert.That(table.Invite(2, 3, 0), Is.False);
        Assert.That(table.IsPending(1, 2), Is.True);
        Assert.That(table.IsPending(2, 1), Is.False);
        Assert.That(table.Play(1, RpsRules.Rock, out _), Is.False); // not started
        Assert.That(table.Start(2, 1, 0), Is.False);
        Assert.That(table.Start(1, 2, 0), Is.True);
        Assert.That(table.Start(1, 2, 0), Is.False);
        Assert.That(table.GamePartner(2), Is.EqualTo(1));
        Assert.That(table.RemovePending(1, 2), Is.False); // started

        Assert.That(table.Play(1, 5, out _), Is.False);
        Assert.That(table.Play(1, RpsRules.Paper, out RpsMatchTable.Played? first), Is.True);
        Assert.That(first, Is.Null);
        Assert.That(table.Play(1, RpsRules.Rock, out _), Is.False); // one card each
        Assert.That(table.Play(2, RpsRules.Scissors, out RpsMatchTable.Played? done), Is.True);
        Assert.That(done, Is.EqualTo(new RpsMatchTable.Played(1, RpsRules.Paper, 2, RpsRules.Scissors)));
        Assert.That(table.IsBusy(1) || table.IsBusy(2), Is.False);
    }

    [Test]
    public void Table_DeclineAndLeave() {
        var table = new RpsMatchTable();
        table.Invite(1, 2, 0);
        Assert.That(table.RemovePending(2, 1), Is.False);
        Assert.That(table.RemovePending(1, 2), Is.True);
        Assert.That(table.IsBusy(1), Is.False);

        Assert.That(table.Invite(1, 2, MicrogameLimits.InviteCooldownMs), Is.True);
        table.Start(1, 2, MicrogameLimits.InviteCooldownMs);
        Assert.That(table.Remove(3), Is.EqualTo(0));
        Assert.That(table.Remove(2), Is.EqualTo(1));
        Assert.That(table.IsBusy(1), Is.False);
    }

    [Test]
    public void Table_InviteCooldown() {
        var table = new RpsMatchTable();
        Assert.That(table.Invite(1, 2, 0), Is.True);
        table.RemovePending(1, 2);
        Assert.That(table.Invite(1, 2, MicrogameLimits.InviteCooldownMs - 1), Is.False);
        Assert.That(table.Invite(2, 1, 1), Is.True); // per inviter
        table.RemovePending(2, 1);
        Assert.That(table.Invite(1, 2, MicrogameLimits.InviteCooldownMs), Is.True);
    }

    [Test]
    public void Table_Expire() {
        var table = new RpsMatchTable();
        table.Invite(1, 2, 0);
        Assert.That(table.Expire(MicrogameLimits.PendingMs - 1), Is.Empty);
        Assert.That(table.Expire(MicrogameLimits.PendingMs), Is.EqualTo(new[] { new RpsMatchTable.Expired(1, 2, false) }));

        long start = 100_000;
        table.Invite(1, 2, start);
        table.Start(1, 2, start + 30_000);
        table.Play(1, RpsRules.Rock, out _);
        Assert.That(table.Expire(start + MicrogameLimits.RoundMs), Is.Empty);
        Assert.That(table.Expire(start + 30_000 + MicrogameLimits.RoundMs), Is.EqualTo(new[] { new RpsMatchTable.Expired(1, 2, true) }));
        Assert.That(table.IsBusy(1) || table.IsBusy(2), Is.False);
    }

    [Test]
    public void Table_ExpireIdleReturnsEmpty() {
        var table = new RpsMatchTable();
        Assert.That(table.Expire(0), Is.Empty);
        table.Invite(1, 2, 0);
        table.RemovePending(1, 2);
        // Only the cooldown is left: pruned once it is over, then the table is idle again.
        Assert.That(table.Expire(MicrogameLimits.InviteCooldownMs), Is.Empty);
        Assert.That(table.Invite(1, 2, MicrogameLimits.InviteCooldownMs), Is.True);
    }

    [TestCase(0, 0, true)]   // no items: settled, so the row is marked claimed
    [TestCase(2, 0, false)]  // nothing given or mailed: retry later
    [TestCase(2, 1, true)]
    [TestCase(2, 2, true)]
    public void ClaimSettled_EmptyRowsSettle(int rewards, int delivered, bool expected) {
        Assert.That(RpsRules.ClaimSettled(rewards, delivered), Is.EqualTo(expected));
    }

    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    // MicroGameManagerCli: a short mode, then the mode's fields.
    [Test]
    public void Packets_Layout() {
        Assert.That(Body(MicrogamePacket.Banner()), Is.EqualTo(new byte[] { 0, 0 }));
        Assert.That(Body(MicrogamePacket.Round()), Is.EqualTo(new byte[] { 4, 0 }));
        byte[] invite = Body(MicrogamePacket.Invite(0x0102030405060708));
        Assert.That(BitConverter.ToInt16(invite, 0), Is.EqualTo(1));
        Assert.That(BitConverter.ToInt64(invite, 2), Is.EqualTo(0x0102030405060708));
        Assert.That(invite, Has.Length.EqualTo(10));
        Assert.That(BitConverter.ToInt16(Body(MicrogamePacket.InviteSent(7)), 0), Is.EqualTo(0xC));
        Assert.That(BitConverter.ToInt16(Body(MicrogamePacket.Accepted(7)), 0), Is.EqualTo(2));
        Assert.That(BitConverter.ToInt16(Body(MicrogamePacket.Declined(7)), 0), Is.EqualTo(3));
        Assert.That(BitConverter.ToInt16(Body(MicrogamePacket.Cancelled(7)), 0), Is.EqualTo(5));

        byte[] failed = Body(MicrogamePacket.Failed(9, RpsRules.ReasonAcceptPeerState));
        Assert.That(BitConverter.ToInt16(failed, 0), Is.EqualTo(6));
        Assert.That(BitConverter.ToInt64(failed, 2), Is.EqualTo(9));
        Assert.That(failed[10], Is.EqualTo(2));
        Assert.That(failed, Has.Length.EqualTo(11));

        Assert.That(Body(MicrogamePacket.Result(RpsRules.Win, RpsRules.Paper, RpsRules.Rock)), Is.EqualTo(new byte[] { 0xB, 0, 1, 1, 0 }));

        byte[] rewards = Body(MicrogamePacket.Rewards([new RewardItem(20000001, 3, 5)]));
        Assert.That(BitConverter.ToInt16(rewards, 0), Is.EqualTo(10));
        Assert.That(BitConverter.ToInt32(rewards, 2), Is.EqualTo(1));
        Assert.That(BitConverter.ToInt32(rewards, 6), Is.EqualTo(20000001));
        Assert.That(BitConverter.ToInt16(rewards, 10), Is.EqualTo(3));
        Assert.That(rewards, Has.Length.EqualTo(12));
    }

    // RpsContext::Read (gameeventcontext.cpp): per reward the play count, then an item count, then the items.
    [Test]
    public void GameEvent_RpsWritesAnItemCountPerReward() {
        var rps = new Rps(30000001, [
            new Rps.RewardData(5, [new RewardItem(20000001, 1, 2), new RewardItem(20000002, 2, 1)]),
            new Rps.RewardData(10, [new RewardItem(20000003, 3, 1)]),
        ], "html");
        var metadata = new GameEventMetadata(900001, GameEventType.RPS, DateTime.UnixEpoch, DateTime.UnixEpoch.AddSeconds(1000),
            TimeSpan.Zero, TimeSpan.Zero, [], rps, "", "", "", "");
        var writer = new ByteWriter();
        new GameEvent(metadata).WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("RPS"));
        Assert.That(reader.ReadInt(), Is.EqualTo(900001));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("html"));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
        reader.Skip(10);
        Assert.That(reader.ReadInt(), Is.EqualTo(20000002));
        reader.Skip(10);
        Assert.That(reader.ReadInt(), Is.EqualTo(10));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000003));
        reader.Skip(10);
        Assert.That(reader.ReadInt(), Is.EqualTo(30000001));
        Assert.That(reader.ReadInt(), Is.EqualTo(900001));
        Assert.That(reader.ReadLong(), Is.EqualTo(1000));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }
}

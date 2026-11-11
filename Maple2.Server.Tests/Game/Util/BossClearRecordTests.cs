using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using BossClearPlayer = Maple2.Server.Game.Manager.Field.DungeonFieldManager.BossClearPlayer;

namespace Maple2.Server.Tests.Game.Util;

// Which room clears are kept for the boss boards, and with whom. Made-up ids.
public class BossClearRecordTests {
    private static DungeonRoomMetadata Room(DungeonGroupType group, int bossRanking) {
        var limit = new DungeonRoomLimitMetadata(0, 4, 0, 0, 0, false, [], [], [], false, false, false, false, false, 0);
        var reward = new DungeonRoomRewardMetadata(false, 0, 0, 0, 0, 0, [], [], 0, 0, 0);
        return new DungeonRoomMetadata(23900001, 0, DungeonPlayType.none, group, DungeonCooldownType.none, 0, 0, 0, [], reward, limit,
            0, 0, 0, false, 0, 0, false, [], [], false, default, false, bossRanking);
    }

    [TestCase(DungeonGroupType.raid)]
    [TestCase(DungeonGroupType.chaosRaid)]
    [TestCase(DungeonGroupType.reverseRaid)]
    [TestCase(DungeonGroupType.lapenta)]
    [TestCase(DungeonGroupType.turka)]
    public void KeepsBossClear_TheBossBoardGroups_WithABossRanking(DungeonGroupType group) {
        Assert.That(DungeonFieldManager.KeepsBossClear(Room(group, 70001)), Is.True);
        Assert.That(DungeonFieldManager.KeepsBossClear(Room(group, 0)), Is.False);
    }

    [TestCase(DungeonGroupType.fameChallenge)]
    [TestCase(DungeonGroupType.colosseum)]
    [TestCase(DungeonGroupType.normal)]
    public void KeepsBossClear_NotOtherGroups(DungeonGroupType group) {
        Assert.That(DungeonFieldManager.KeepsBossClear(Room(group, 70001)), Is.False);
    }

    [Test]
    public void Party_OnlyTheRoomPartyMembersInTheField_WithItsLeader() {
        var clear = DungeonFieldManager.BossClearParty(501, 11, [
            new BossClearPlayer(11, 501, 12),
            new BossClearPlayer(13, 777, 13),
            new BossClearPlayer(12, 501, 12),
            new BossClearPlayer(14, 0, 0),
        ]);
        Assert.That(clear, Is.Not.Null);
        Assert.That(clear!.Value.LeaderId, Is.EqualTo(12));
        Assert.That(clear.Value.Members, Is.EqualTo(new long[] { 11, 12 }));
    }

    [Test]
    public void Party_LeaderNotInTheField_LedByTheFirstMember() {
        var clear = DungeonFieldManager.BossClearParty(501, 11, [
            new BossClearPlayer(13, 501, 99),
            new BossClearPlayer(11, 501, 99),
        ]);
        Assert.That(clear, Is.Not.Null);
        Assert.That(clear!.Value.LeaderId, Is.EqualTo(13));
        Assert.That(clear.Value.Members, Is.EqualTo(new long[] { 13, 11 }));
    }

    [Test]
    public void Party_NoneOfTheRoomPartyLeft_TheFirstUserAlone() {
        var clear = DungeonFieldManager.BossClearParty(501, 11, [new BossClearPlayer(13, 777, 13), new BossClearPlayer(11, 0, 0)]);
        Assert.That(clear, Is.Not.Null);
        Assert.That(clear!.Value.LeaderId, Is.EqualTo(11));
        Assert.That(clear.Value.Members, Is.EqualTo(new long[] { 11 }));
    }

    [Test]
    public void Party_NoneOfTheRoomPartyAndNoFirstUser_KeepsNothing() {
        Assert.That(DungeonFieldManager.BossClearParty(501, 11, [new BossClearPlayer(13, 777, 13)]), Is.Null);
        Assert.That(DungeonFieldManager.BossClearParty(501, 11, []), Is.Null);
    }

    [Test]
    public void NoParty_TheFirstUserAlone() {
        var clear = DungeonFieldManager.BossClearParty(0, 11, [new BossClearPlayer(14, 0, 0), new BossClearPlayer(11, 0, 0)]);
        Assert.That(clear, Is.Not.Null);
        Assert.That(clear!.Value.LeaderId, Is.EqualTo(11));
        Assert.That(clear.Value.Members, Is.EqualTo(new long[] { 11 }));
    }

    [Test]
    public void NoParty_FirstUserJoinedAPartyInside_StillAlone() {
        var clear = DungeonFieldManager.BossClearParty(0, 11, [new BossClearPlayer(11, 501, 14), new BossClearPlayer(14, 501, 14)]);
        Assert.That(clear, Is.Not.Null);
        Assert.That(clear!.Value.Members, Is.EqualTo(new long[] { 11 }));
    }

    [Test]
    public void NoParty_FirstUserGone_KeepsNothing() {
        Assert.That(DungeonFieldManager.BossClearParty(0, 11, [new BossClearPlayer(14, 0, 0)]), Is.Null);
    }

    [Test]
    public void TryKeepBossClear_OncePerRoom() {
        var record = new DungeonRoomRecord(null!);
        Assert.That(record.TryKeepBossClear(), Is.True);
        Assert.That(record.TryKeepBossClear(), Is.False);
        Assert.That(record.TryKeepBossClear(), Is.False);
        Assert.That(new DungeonRoomRecord(null!).TryKeepBossClear(), Is.True);
    }
}

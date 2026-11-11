using System;
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

    [Test]
    public void KeepsBossClear_OnlyTheBoardGroupsWithABossRanking() {
        foreach (DungeonGroupType group in Enum.GetValues<DungeonGroupType>()) {
            bool board = group is DungeonGroupType.raid or DungeonGroupType.chaosRaid or DungeonGroupType.reverseRaid
                or DungeonGroupType.lapenta or DungeonGroupType.turka;
            Assert.That(DungeonFieldManager.KeepsBossClear(Room(group, 70001)), Is.EqualTo(board), group.ToString());
            Assert.That(DungeonFieldManager.KeepsBossClear(Room(group, 0)), Is.False, group.ToString());
        }
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
    public void Party_NoneOfTheRoomPartyLeft_KeepsNothing() {
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
    public void NoParty_FirstUserGoneOrInAParty_KeepsNothing() {
        Assert.That(DungeonFieldManager.BossClearParty(0, 11, [new BossClearPlayer(14, 0, 0)]), Is.Null);
        Assert.That(DungeonFieldManager.BossClearParty(0, 11, [new BossClearPlayer(11, 501, 11)]), Is.Null);
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;

namespace Maple2.Server.Tests.Game.Manager;

// The helper reward of a veteran who cleared with a rookie (DungeonManager.GiveHelperReward): one count of the
// helper reward room's record while it has one left, the helper flag on the result, and the two notices sent with
// the other grants. The rooms are made up; 1003 is the helper reward room the manager names.
public class DungeonHelperRewardTests {
    private const int HelperRoom = 1003;
    private const int Room = 23050201;
    private const long Clear = 1_800_000_000;

    private TestGameSession test = null!;

    private void Start(int rewardCount) {
        test = new TestGameSession();
        DungeonRoomMetadata helper = TestGameSession.DungeonRoom(HelperRoom, 50000016, rewardCount: rewardCount) with { CooldownType = DungeonCooldownType.nextDay };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> {
            [HelperRoom] = helper,
            [Room] = TestGameSession.DungeonRoom(Room, 50000016),
        }));
        test.Start();
        test.EnterDungeon(test.Session.TableMetadata.DungeonRoomTable.Entries[Room]);
        test.TakeSent();
    }

    private List<Action> Give() {
        var grants = new List<Action>();
        typeof(Maple2.Server.Game.Manager.DungeonManager).GetMethod("GiveHelperReward", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(test.Session.Dungeon, [Clear, grants]);
        return grants;
    }

    [Test]
    public void ARecordWithACountLeft_TakesOneAndFlagsTheResult() {
        Start(rewardCount: 2);

        List<Action> grants = Give();

        DungeonRecord record = test.Session.Dungeon.Records[HelperRoom];
        Assert.That((record.UnionClears, record.UnionSubClears), Is.EqualTo(((byte) 1, (byte) 1)));
        Assert.That(record.UnionCooldownTimestamp, Is.GreaterThan(Clear));
        Assert.That(test.Session.Dungeon.UserRecord!.Flag.HasFlag(DungeonBonusFlag.Helper), Is.True);
        Assert.That(test.TakeSent(), Is.Empty, "told only with the grants");

        grants.ForEach(grant => grant());
        Assert.That(test.TakeSent().Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.RoomDungeon, SendOp.RoomDungeon }));
    }

    [Test]
    public void ARecordWithNoCountLeft_GivesNothing() {
        Start(rewardCount: 1);
        Give();
        test.Session.Dungeon.UserRecord!.Flag = 0;

        List<Action> grants = Give();

        Assert.That(grants, Is.Empty);
        Assert.That(test.Session.Dungeon.Records[HelperRoom].UnionClears, Is.EqualTo(1));
        Assert.That(test.Session.Dungeon.UserRecord.Flag.HasFlag(DungeonBonusFlag.Helper), Is.False);
    }
}

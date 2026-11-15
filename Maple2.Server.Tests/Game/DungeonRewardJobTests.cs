using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game;

// A clear's global reward box is rolled for the player, with the player's job (ItemDropTable::Drop sends a global
// box to DropGlobal with the context it gives an individual box, and DropGlobal leaves out gear recommended only for
// other jobs). KR room 21001001 lists global box 10, whose groups 3 and 4 hold every job's level 14-16 gear, such as
// the Berserker greatsword 15000081 and the Assassin weapon 13400074; without the job an Assassin got the greatsword.
public class DungeonRewardJobTests {
    private const int RoomId = 21001001;
    private const int GlobalBoxId = 10;
    private const int BerserkerItem = 15000081;
    private const int AssassinItem = 13400074;

    private static GlobalDropItemBoxTable.Group Group(int groupId) =>
        new(groupId, 0, 0, [new GlobalDropItemBoxTable.Group.DropCount(1, 100)], true, (MapType) 0, (Continent) 0);

    private static GlobalDropItemBoxTable.Item Row(int itemId) =>
        new(itemId, 1, 99, new GlobalDropItemBoxTable.Range<int>(1, 1), 1, 1, [], false);

    [Test]
    public void ClearReward_GlobalBox_LeavesOutOtherJobsGear() {
        var test = new TestGameSession();
        foreach ((int id, JobCode job) in new[] { (BerserkerItem, JobCode.Berserker), (AssassinItem, JobCode.Assassin) }) {
            ItemMetadata data = TestGameSession.Metadata(id);
            test.Register(data with { Limit = data.Limit with { JobRecommends = [job] } });
        }
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(RoomId, 90000301) with { GroupType = DungeonGroupType.normal };
        room = room with { Reward = room.Reward with { UnlimitedDropBoxIds = [GlobalBoxId] } };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [RoomId] = room }));
        test.SetTable(new DungeonConfigTable(new Dictionary<int, int>(),
            new Dictionary<int, DungeonMissionRankMetadata> { [1] = new(1, string.Empty, 100, []) }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        // One group of the Berserker's gear only, one of the Assassin's only: each always gives one drop.
        test.SetServerTable(new GlobalDropItemBoxTable(
            new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>> {
                [GlobalBoxId] = new() { [3] = [Group(3)], [4] = [Group(4)] },
            },
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>> { [3] = [Row(BerserkerItem)], [4] = [Row(AssassinItem)] }));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.Start();
        test.Session.Player.Value.Character.Job = Job.Assassin;

        DungeonRoomRecord record = test.EnterDungeon(room);
        record.UserResults[test.CharacterId] = test.Session.Dungeon.UserRecord!;
        ((DungeonFieldManager) test.Session.Field!).ChangeState(DungeonState.Clear);

        Assert.That(test.Session.Dungeon.UserRecord!.RewardItems.Select(item => item.ItemId), Is.EquivalentTo(new[] { AssassinItem }));
    }
}

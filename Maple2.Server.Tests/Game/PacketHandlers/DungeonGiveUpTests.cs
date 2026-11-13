using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// DungeonReward 5 (the give-up button of the dungeon room, StageDungeon): the client shows it to the party leader
// once the room's script allows it, and sends the mode byte alone. The server tells the room (DungeonMission 5) and
// fails it; before the script allows it the answer is RoomDungeon error 24 (s_room_dungeon_cannot_giveup_yet).
public class DungeonGiveUpTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Room = 20003001;

    private TestGameSession test = null!;
    private DungeonRoomRecord record = null!;
    private DungeonFieldManager field = null!;

    private void Start(bool leader = true, bool enabled = true, DungeonState state = DungeonState.None) {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 2000023);
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.Start();
        record = test.EnterDungeon(room);
        record.UserResults[test.CharacterId] = test.Session.Dungeon.UserRecord!;
        record.State = state;
        field = (DungeonFieldManager) test.Session.Field!;
        field.GiveUpEnabled = enabled;
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        test.Session.Party = new PartyManager(test.World, test.Session);
        var party = new Party(1, test.AccountId, leader ? test.CharacterId : test.CharacterId + 1, "leader");
        typeof(PartyManager).GetField("<Party>k__BackingField", Any)!.SetValue(test.Session.Party, party);
        test.TakeSent();
    }

    private List<byte[]> GiveUp() {
        new DungeonRewardHandler().Handle(test.Session, TestGameSession.Packet(writer => writer.WriteByte(5)));
        return test.TakeSent();
    }

    [Test]
    public void TheLeaderFailsTheRoomAndTheRoomIsTold() {
        Start();
        List<byte[]> sent = GiveUp();
        // DungeonMission with the mode byte 5 alone, then the fail's Stage(1).
        Assert.That((TestGameSession.Op(sent[0]), sent[0][2..]), Is.EqualTo((SendOp.DungeonMission, new byte[] { 5 })));
        Assert.That(sent.Skip(1).Select(System.Convert.ToHexString), Does.Contain(System.Convert.ToHexString(DungeonMissionPacket.Stage(1).ToArray())));
        Assert.That(record.State, Is.EqualTo(DungeonState.Fail));
    }

    [Test]
    public void TooEarlyIsError24() {
        Start(enabled: false);
        List<byte[]> sent = GiveUp();
        Assert.That(sent.Select(System.Convert.ToHexString),
            Is.EqualTo(new[] { System.Convert.ToHexString(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_cannot_giveup_yet).ToArray()) }));
        Assert.That((byte) DungeonRoomError.s_room_dungeon_cannot_giveup_yet, Is.EqualTo(24));
        Assert.That(record.State, Is.EqualTo(DungeonState.None));
    }

    [TestCase(false, DungeonState.None)] // a member who is not the leader
    [TestCase(true, DungeonState.Clear)] // the room has its result
    public void NothingElseChanges(bool leader, DungeonState state) {
        Start(leader: leader, state: state);
        Assert.That(GiveUp(), Is.Empty);
        Assert.That(record.State, Is.EqualTo(state));
    }
}

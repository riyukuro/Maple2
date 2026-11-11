using Maple2.Model.Game.Dungeon;
using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// set_dungeon_variable / dungeon_variable: one store per dungeon instance; an id never set reads 0. Made-up ids.
public class TriggerDungeonVariableTests {
    [Test]
    public void UnsetId_ReadsZero() {
        var record = new DungeonRoomRecord(null!);
        Assert.That(TriggerContext.DungeonVariableEquals(record.Variables, 901, 0), Is.True);
        Assert.That(TriggerContext.DungeonVariableEquals(record.Variables, 901, 1), Is.False);
    }

    [Test]
    public void ValueSetThroughOneFieldIsSeenThroughTheSharedRecord() {
        var record = new DungeonRoomRecord(null!);
        // The lobby and a room field hold the same record.
        DungeonRoomRecord lobbyView = record, roomView = record;
        lobbyView.Variables[911] = 1;
        Assert.That(TriggerContext.DungeonVariableEquals(roomView.Variables, 911, 1), Is.True);
        Assert.That(TriggerContext.DungeonVariableEquals(new DungeonRoomRecord(null!).Variables, 911, 1), Is.False);
    }
}

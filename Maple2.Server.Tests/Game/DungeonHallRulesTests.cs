using Maple2.Model.Error;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game;

// RoomDungeon 4/12/13/14, the hall buttons of the dungeon search window.
public class DungeonHallRulesTests {
    private static readonly ConstantsTable Constants = new ConstantsTable() with {
        ChaosDungeonHallFieldID = 63000047,
        ReverseRaidDungeonHallFieldID = 63000054,
        LapentaDungeonHallFieldID = 63000063,
        ColosseumDungeonHallFieldID = 83000001,
    };

    [TestCase(DungeonHallRules.ChaosRaid, 63000047)]
    [TestCase(DungeonHallRules.ReverseRaid, 63000054)]
    [TestCase(DungeonHallRules.Lapenta, 63000063)]
    [TestCase(DungeonHallRules.Colosseum, 83000001)]
    [TestCase((byte) 3, 0)]
    [TestCase((byte) 9, 0)]
    public void HallOfEachMode(byte mode, int fieldId) {
        Assert.That(DungeonHallRules.HallFieldId(mode, Constants), Is.EqualTo(fieldId));
    }

    [Test]
    public void UnsetConstantHasNoHall() {
        Assert.That(DungeonHallRules.HallFieldId(DungeonHallRules.ChaosRaid, new ConstantsTable()), Is.EqualTo(0));
    }

    [Test]
    public void OpenFieldMoves() {
        Assert.That(DungeonHallRules.Error(DungeonHallRules.ChaosRaid, 63000047, 2000062, 0), Is.Null);
        // Another group's hall is no refusal either.
        Assert.That(DungeonHallRules.Error(DungeonHallRules.Lapenta, 63000063, 63000047, 0), Is.Null);
    }

    [TestCase(DungeonHallRules.ChaosRaid, 63000047, DungeonRoomError.s_room_dungeon_AlreadyChaosHall)]
    [TestCase(DungeonHallRules.ReverseRaid, 63000054, DungeonRoomError.s_room_dungeon_AlreadyChaosHall)]
    [TestCase(DungeonHallRules.Lapenta, 63000063, DungeonRoomError.s_room_dungeon_AlreadyLapentaHall)]
    [TestCase(DungeonHallRules.Colosseum, 83000001, DungeonRoomError.s_room_dungeon_AlreadyColosseumHall)]
    public void AlreadyInTheHall(byte mode, int hall, DungeonRoomError error) {
        Assert.That(DungeonHallRules.Error(mode, hall, hall, 0), Is.EqualTo(error));
        Assert.That((int) error, Is.EqualTo(mode switch {
            DungeonHallRules.Lapenta => 0x0e,
            DungeonHallRules.Colosseum => 0x0f,
            _ => 0x0d,
        }));
    }

    [Test]
    public void InsideDungeonComesFirst() {
        DungeonRoomError? error = DungeonHallRules.Error(DungeonHallRules.ChaosRaid, 63000047, 2000062, 23001001);
        Assert.That(error, Is.EqualTo(DungeonRoomError.s_room_dungeon_CantEnterInDungeon));
        Assert.That((int) error!.Value, Is.EqualTo(0x10));
    }

    [Test]
    public void ReturnPositionKeptOnlyOnTheReturnMap() {
        // Pressed on an open field that is its own return map: the hall's exit leads back to that spot.
        Assert.That(DungeonHallRules.KeepsReturnPosition(2000062, 2000062), Is.True);
        // Pressed in another hall or a field returning to a town: the spot is on another map.
        Assert.That(DungeonHallRules.KeepsReturnPosition(63000047, 2000062), Is.False);
        Assert.That(DungeonHallRules.KeepsReturnPosition(0, 0), Is.False);
    }
}

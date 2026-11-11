using System.Collections.Generic;
using Maple2.Server.Game.Util;
using Maple2.Tools;

namespace Maple2.Server.Tests.Game.Util;

// 84000003_wd main: after the studio timer, room_expire sends everyone in the room back.
public class RoomExpiryTests {
    [Test]
    public void ReturnsEveryPlayerOnce() {
        var players = new List<int> { 1, 2, 3 };
        var returned = new List<int>();
        int count = RoomExpiry.ReturnAll(players, returned.Add);
        Assert.Multiple(() => {
            Assert.That(count, Is.EqualTo(3));
            Assert.That(returned, Is.EqualTo(new[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void LeavingTheFieldDuringReturnIsSafe() {
        var players = new Dictionary<int, string> { [1] = "a", [2] = "b" };
        var returned = new List<string>();
        int count = RoomExpiry.ReturnAll(players.Values, name => {
            returned.Add(name);
            players.Remove(name == "a" ? 1 : 2);
        });
        Assert.Multiple(() => {
            Assert.That(count, Is.EqualTo(2));
            Assert.That(returned, Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(players, Is.Empty);
        });
    }

    [Test]
    public void EmptyRoomReturnsNobody() {
        int calls = 0;
        Assert.That(RoomExpiry.ReturnAll(new List<int>(), _ => calls++), Is.EqualTo(0));
        Assert.That(calls, Is.EqualTo(0));
    }

    [Test]
    public void TheRoomItselfIsNotTheReturnMap() {
        var returnMaps = new LimitedStack<int>(3);
        returnMaps.Push(2000001);
        returnMaps.Push(84000003);
        RoomExpiry.DropOwnReturn(returnMaps, 84000003, 2000062);
        Assert.That(returnMaps.Peek(), Is.EqualTo(2000001));
    }

    [Test]
    public void OnlyTheRoomLeftGoesToTheDefaultMap() {
        var returnMaps = new LimitedStack<int>(3);
        returnMaps.Push(84000006);
        returnMaps.Push(84000006);
        RoomExpiry.DropOwnReturn(returnMaps, 84000006, 2000062);
        Assert.Multiple(() => {
            Assert.That(returnMaps.Peek(), Is.EqualTo(2000062));
            Assert.That(returnMaps.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void AnotherReturnMapIsKept() {
        var returnMaps = new LimitedStack<int>(3);
        returnMaps.Push(2000001);
        RoomExpiry.DropOwnReturn(returnMaps, 84000003, 2000062);
        Assert.That(returnMaps.Peek(), Is.EqualTo(2000001));
    }
}

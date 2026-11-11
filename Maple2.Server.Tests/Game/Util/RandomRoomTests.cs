using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.Table.Server;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The AI createRandomRoom node and table/Server/room_random.xml (made-up rows in the shipped shape).
public class RandomRoomTests {
    private static readonly RandomRoomMetadata.Ref[] Refs = [new(301, 100), new(303, 100), new(304, 0), new(305, 50)];

    [TestCase(1500, 0, true)]
    [TestCase(1500, 1499, true)]
    [TestCase(1500, 1500, false)]
    [TestCase(10000, 9999, true)]
    [TestCase(0, 0, false)]
    public void Opens(int prob, int roll, bool expected) {
        Assert.That(RandomRoomRules.Opens(prob, roll), Is.EqualTo(expected));
    }

    // A weight of 0 is never picked.
    [TestCase(0, 301)]
    [TestCase(99, 301)]
    [TestCase(100, 303)]
    [TestCase(199, 303)]
    [TestCase(200, 305)]
    [TestCase(249, 305)]
    [TestCase(250, null)]
    public void PickRoom(int roll, int? expected) {
        Assert.Multiple(() => {
            Assert.That(RandomRoomRules.TotalWeight(Refs), Is.EqualTo(250));
            Assert.That(RandomRoomRules.PickRoom(Refs, roll), Is.EqualTo(expected));
        });
    }

    [Test]
    public void NoWeightNoRoom() {
        Assert.Multiple(() => {
            Assert.That(RandomRoomRules.TotalWeight([]), Is.Zero);
            Assert.That(RandomRoomRules.PickRoom([new RandomRoomMetadata.Ref(301, 0)], 0), Is.Null);
        });
    }

    [TestCase(3, 4, false)]
    [TestCase(4, 4, true)]
    [TestCase(100, 0, false)]
    public void Full(int entered, int maxUserCount, bool expected) {
        Assert.That(RandomRoomRules.Full(entered, maxUserCount), Is.EqualTo(expected));
    }

    [Test]
    public void TableRows() {
        var randomRoom = new RandomRoom {
            id = 3,
            prob = 1500,
            @ref = [new RandomRoom.Ref { roomid = 301, weight = 100 }, new RandomRoom.Ref { roomid = 303, weight = 100 }],
        };
        var room = new RoomRandom {
            id = 301,
            fieldIDs = [3000002],
            roomdurationTick = 40000,
            nifAssetName = "Eff_uw_potral_A01",
            maxUserCount = 4,
            autoClosing = 1,
        };
        RoomRandomTable table = ServerTableMapper.MapRoomRandom([randomRoom, new RandomRoom { id = 9, prob = 10 }], [room]);
        Assert.Multiple(() => {
            Assert.That(table.RandomRooms[3].Prob, Is.EqualTo(1500));
            Assert.That(table.RandomRooms[3].Refs.Select(entry => (entry.RoomId, entry.Weight)),
                Is.EqualTo(new[] { (301, 100), (303, 100) }));
            Assert.That(table.RandomRooms[9].Refs, Is.Empty);
            Assert.That(table.Rooms[301].FieldIds, Is.EqualTo(new[] { 3000002 }));
            Assert.That(table.Rooms[301].DurationTick, Is.EqualTo(40000));
            Assert.That(table.Rooms[301].AssetName, Is.EqualTo("Eff_uw_potral_A01"));
            Assert.That(table.Rooms[301].MaxUserCount, Is.EqualTo(4));
            Assert.That(table.Rooms[301].AutoClosing, Is.True);
        });
    }
}

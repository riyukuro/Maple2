using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The furnishing lists of the game server entry (FurnishingManager.Load): the storage's count, its list (start,
// each stack, end), then the placed cubes' list (start, each cube, end). KR cube 50200902 (Dessert Showcase).
public class FurnishingLoadTests {
    private const int Cube = 50200902;

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [TestCase(0)]
    [TestCase(2)]
    public void Load_SendsTheStorageThenThePlacedCubes(int cubes) {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Cube) with {
            Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false),
            Install = new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none),
        });
        GameSession session = test.Start();
        for (int i = 0; i < cubes; i++) {
            Assert.That(session.Item.Furnishing.AddCube(Cube), Is.Not.EqualTo(0L));
        }
        test.TakeSent();

        session.Item.Furnishing.Load();

        Maple2.Model.Game.Item? stack = session.Item.Furnishing.GetItem(Cube);
        var expected = new List<string> { Hex(FurnishingStoragePacket.Count(cubes > 0 ? 1 : 0)), Hex(FurnishingStoragePacket.StartList()) };
        if (cubes > 0) {
            Assert.That(stack?.Amount, Is.EqualTo(cubes));
            expected.Add(Hex(FurnishingStoragePacket.Add(stack!)));
        }
        expected.AddRange([Hex(FurnishingStoragePacket.EndList()), Hex(FurnishingInventoryPacket.StartList()), Hex(FurnishingInventoryPacket.EndList())]);
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(expected));
        Assert.That(test.Errors, Is.Empty);
    }
}

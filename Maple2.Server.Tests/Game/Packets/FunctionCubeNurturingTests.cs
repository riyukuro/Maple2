using System.Runtime.CompilerServices;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class FunctionCubeNurturingTests {
    private static InteractCube CubeOf(string id) {
        var cube = (InteractCube) RuntimeHelpers.GetUninitializedObject(typeof(InteractCube));
        cube.Id = id;
        return cube;
    }

    // Client: functioncubeobject.cpp:2554-2596 and MyPC::ReadDailyRecords (mypc.cpp:4060): long character id, text
    // cube id, long mark time, int count x long (fed today), int count x long (played today).
    [Test]
    public void FeedCarriesDailyRecord() {
        ByteWriter writer = FunctionCubePacket.Feed(77, CubeOf("4_12"), 1700000000, [501], [601, 602]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.FunctionCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("4_12"));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(501));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(601));
        Assert.That(reader.ReadLong(), Is.EqualTo(602));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + (2 + 8) + 8 + 4 + 8 + 4 + 16));
    }

    // Client: functioncubeobject.cpp:2580: long character id, text cube id, int error.
    [Test]
    public void NurturingErrorIsMode13() {
        ByteWriter writer = FunctionCubePacket.NurturingError(77, CubeOf("4_12"), 4);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.FunctionCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(13));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("4_12"));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + (2 + 8) + 4));
    }

    // Client: MyPC::ReadDailyRecords (mypc.cpp:4060-4070) empties both lists before it reads, so a reply holds
    // every pet of the day. The pet in use is listed with the cube that was used.
    [Test]
    public void DailyList_HoldsEveryPetOfTheDay() {
        NurturingRecord[] played = [
            new NurturingRecord(OwnerId: 5, ItemId: 100, CubeId: 900),
            new NurturingRecord(OwnerId: 6, ItemId: 100, CubeId: 901),
            new NurturingRecord(OwnerId: 7, ItemId: 101, CubeId: 0), // the owner took the pet away
        ];

        // Owner 6 has the item placed twice; cube 950 was used, not the first one (901).
        Assert.That(NurturingRecord.CubeIds(played, ownerId: 6, itemId: 100, cubeId: 950), Is.EqualTo(new long[] { 900, 950 }));
        // A reply for another pet (a feeding of one's own) keeps the played list whole.
        Assert.That(NurturingRecord.CubeIds(played, ownerId: 1, itemId: 100, cubeId: 800), Is.EqualTo(new long[] { 900, 901 }));
        Assert.That(NurturingRecord.CubeIds([], ownerId: 1, itemId: 100, cubeId: 800), Is.Empty);
    }
}

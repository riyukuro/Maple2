using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class BuildOwnerLocksTests {
    [Test]
    public void MissingTrophiesAreTheUnclearedKeysInOrder() {
        (int, int)[] required = [(22300052, 1), (21100051, 7), (21100051, 5), (22300052, 1), (0, 1), (21220003, 2)];

        var missing = HomeBuildRules.MissingTrophies(required,
            (id, grade) => id == 21220003 || (id == 21100051 && grade == 5));

        Assert.That(missing, Is.EqualTo(new[] { (21100051, 7), (22300052, 1) }));
    }

    [Test]
    public void NothingMissingForAnOwnerWithEveryGrade() {
        Assert.That(HomeBuildRules.MissingTrophies([(21100051, 7), (22300052, 1)], (_, _) => true), Is.Empty);
    }

    // ResponseCube 0x37: the level as a short, the count, then (trophy id, grade) ints.
    [Test]
    public void BuildOwnerLocksBytes() {
        var reader = new ByteReader(CubePacket.BuildOwnerLocks(3, [(21100041, 1), (21100051, 5)]).ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(55));
        Assert.That(reader.ReadShort(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(21100041));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(21100051));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // FurnishingStorage 5: the storage item's uid and the amount gained (13 bytes).
    [Test]
    public void StoragePurchaseNamesTheUid() {
        var reader = new ByteReader(FurnishingStoragePacket.Purchase(2607000000000123456, 1).ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.FurnishingStorage));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadLong(), Is.EqualTo(2607000000000123456));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}

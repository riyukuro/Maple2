using System;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Manager;

public class PetBagTests {
    private static long nextUid = 100;

    private static Model.Game.Item CreateItem(int id, int amount) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(id, $"{id}", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        return new Model.Game.Item(metadata, 1, amount) {
            Uid = nextUid++,
        };
    }

    [Test]
    public void TakePieces_ShrinksTheStack() {
        var potion = CreateItem(20000001, 3);
        var bag = new ItemCollection(2);
        bag.Add(potion);

        Assert.That(PetManager.TakePieces(bag, potion.Uid, 1, out Model.Game.Item? item), Is.EqualTo(PetManager.PieceResult.Reduced));
        Assert.That(item, Is.SameAs(potion));
        Assert.That(potion.Amount, Is.EqualTo(2));
        Assert.That(bag.Contains(potion.Uid), Is.True);
    }

    [Test]
    public void TakePieces_LastPieceLeavesTheBag() {
        var potion = CreateItem(20000001, 1);
        var bag = new ItemCollection(2);
        bag.Add(potion);

        Assert.That(PetManager.TakePieces(bag, potion.Uid, 1, out Model.Game.Item? item), Is.EqualTo(PetManager.PieceResult.Removed));
        Assert.That(item, Is.SameAs(potion));
        Assert.That(bag.Contains(potion.Uid), Is.False);
        Assert.That(bag.OpenSlots, Is.EqualTo(2));
    }

    [Test]
    public void TakePieces_RefusesWhatIsNotThere() {
        var potion = CreateItem(20000001, 2);
        var bag = new ItemCollection(2);
        bag.Add(potion);

        Assert.That(PetManager.TakePieces(bag, potion.Uid + 1000, 1, out _), Is.EqualTo(PetManager.PieceResult.None));
        Assert.That(PetManager.TakePieces(bag, potion.Uid, 3, out _), Is.EqualTo(PetManager.PieceResult.None));
        Assert.That(PetManager.TakePieces(bag, potion.Uid, 0, out _), Is.EqualTo(PetManager.PieceResult.None));
        Assert.That(potion.Amount, Is.EqualTo(2));
        Assert.That(bag.Contains(potion.Uid), Is.True);
    }

    private static byte[] Body(ByteWriter writer) {
        return writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();
    }

    // ResponsePet 15: the mode and one bool, the client's pet auto flag.
    [Test]
    public void AutoChanged_IsMode15AndTheFlag() {
        Assert.That(Body(PetPacket.AutoChanged(true)), Is.EqualTo(new byte[] { 15, 1 }));
        Assert.That(Body(PetPacket.AutoChanged(false)), Is.EqualTo(new byte[] { 15, 0 }));
    }

    // Level 23-50 class 1 monsters and the two class 3 ones, with the exp KMS2 gives a level 1 pet.
    [TestCase(23, 1, 2)]
    [TestCase(24, 1, 2)]
    [TestCase(39, 1, 4)]
    [TestCase(40, 1, 4)]
    [TestCase(41, 1, 4)]
    [TestCase(42, 1, 5)]
    [TestCase(44, 1, 5)]
    [TestCase(50, 1, 6)]
    [TestCase(40, 3, 19)]
    [TestCase(42, 3, 20)]
    [TestCase(0, 1, 0)]
    public void KillExp(int level, int npcClass, long expected) {
        Assert.That(PetFusionRules.KillExp(level, npcClass), Is.EqualTo(expected));
    }
}

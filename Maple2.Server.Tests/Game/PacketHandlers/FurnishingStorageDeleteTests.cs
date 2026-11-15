using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Deleting a stored maid item (FurnishingStorage delete). Its maid's row goes at once, so the item's emptied row is
// stored before it: left to the next save, a crash brought the item back without its maid. The item id is made up.
public class FurnishingStorageDeleteTests {
    private const int MaidCube = 50200921;
    private TestGameSession test = null!;

    private (GameSession Session, long Uid) Start() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(MaidCube) with {
            Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false, 3),
            Install = new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none),
        });
        GameSession session = test.Start();
        long uid = session.Item.Furnishing.AddCube(MaidCube);
        Assert.That(uid, Is.Not.Zero);
        test.TakeSent();
        return (session, uid);
    }

    private int StoredAmount(long uid) {
        using var context = new Ms2Context(test.Options);
        return context.Item.AsNoTracking().Single(row => row.Id == uid).Amount;
    }

    [Test]
    public void AMaidItem_StoresItsEmptiedRow() {
        (GameSession session, long uid) = Start();
        session.Item.Furnishing.RemoveItem(uid);

        Assert.Multiple(() => {
            Assert.That(StoredAmount(uid), Is.Zero);
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(Convert.ToHexString(FurnishingStoragePacket.Remove(uid).ToArray())));
        });
    }

    // The emptied row not stored: the item is kept and its maid is not deleted.
    [Test]
    public void AMaidItem_RowNotStored_IsKept() {
        (GameSession session, long uid) = Start();
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Modified && entry.Entity.GetType().Name == "Item");
        session.Item.Furnishing.RemoveItem(uid);

        Assert.Multiple(() => {
            Assert.That(session.Item.Furnishing.GetItem(MaidCube)?.Amount, Is.EqualTo(1));
            Assert.That(StoredAmount(uid), Is.EqualTo(1));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }
}

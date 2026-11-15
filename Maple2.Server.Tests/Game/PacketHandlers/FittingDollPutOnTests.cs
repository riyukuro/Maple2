using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// FittingDoll 2, put on: an inventory item goes on the doll, and the doll item it takes off becomes the character's in
// the same save. The ids are made up.
public class FittingDollPutOnTests {
    private const int DollItem = 50400001;
    private const int DollTop = 11400001;
    private const int HeldTop = 11400002;
    private const long PlotId = 7001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Plot plot = null!;
    private FieldFittingDoll doll = null!;

    private static ItemMetadata Gear(int id, EquipSlot slot, int subType = 1, Gender gender = Gender.All,
                                     TransferType transfer = TransferType.Tradable) {
        ItemMetadata metadata = TestGameSession.Metadata(id, slot, gender);
        return metadata with {
            Property = metadata.Property with { SubType = subType },
            Limit = metadata.Limit with { TransferType = transfer },
        };
    }

    // A male equipment doll placed in the session's home wearing |onDoll| in its first slot, the character holding
    // |held| in the inventory.
    private void Start(ItemMetadata onDoll, ItemMetadata held) {
        test = new TestGameSession();
        test.Store(held);
        test.Start();
        plot = test.EnterHome();
        plot.Id = PlotId;
        ItemMetadata dollMetadata = TestGameSession.Metadata(DollItem);
        test.Register(dollMetadata, onDoll);
        PlotCube cube;
        using (GameStorage.Request db = test.Storage.Context()) {
            cube = db.CreateCube(plot, new PlotCube(dollMetadata) { Position = new Vector3B(1, 2, 3), Type = PlotCube.CubeType.Construction })!;
            Item item = db.CreateItem(cube.Id, new Item(onDoll) {
                Group = ItemGroup.FittingDoll,
                Slot = (short) onDoll.SlotNames[0],
            })!;
            doll = new FieldFittingDoll(9000, cube, plot, new FunctionCubeMetadata.FittingDollData(Gender.Male, 0));
            doll.Items[onDoll.SlotNames[0]] = item;
        }
        // The test field is made without its constructor, so the doll table is set here.
        FieldInfo field = typeof(FieldManager).GetField("fittingDolls", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var dolls = new ConcurrentDictionary<long, FieldFittingDoll> { [cube.Id] = doll };
        field.SetValue(session.Field, dolls);
        test.TakeSent();
    }

    private List<byte[]> PutOn(Item item, EquipSlot slot) {
        new FittingDollHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(doll.Uid);
            writer.WriteLong(item.Uid);
            writer.WriteByte((byte) slot);
        }));
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.FittingDoll).ToList();
    }

    private static string Error(int error) {
        var writer = Packet.Of(SendOp.FittingDoll);
        writer.WriteByte(11);
        writer.WriteInt(error);
        return Convert.ToHexString(writer.ToArray());
    }

    private Dictionary<int, (long Owner, ItemGroup Group)> Rows() {
        using var context = new Ms2Context(test.Options);
        return context.Item.AsNoTracking().ToList().ToDictionary(row => row.ItemId, row => (row.OwnerId, row.Group));
    }

    private Item Held() => session.Item.Inventory.Find(HeldTop).Single();
    private int OnDoll(EquipSlot slot) => doll.Items.TryGetValue(slot, out Item? item) ? item.Id : 0;

    // The doll item taken off has left the doll in the DB with the put-on's own save: every save after the one that
    // puts the held item on the doll fails (a crash right after it). Before, the item taken off moved in a write of
    // its own after the put-on's, and its row stayed on the doll's slot beside the one put on.
    [Test]
    public void TheItemTakenOffMovesInThePutOnsSave() {
        Start(Gear(DollTop, EquipSlot.CL), Gear(HeldTop, EquipSlot.CL));
        Item held = Held();
        bool putOn = false;
        test.FailSave = context => {
            if (putOn) {
                return true;
            }
            putOn = context.ChangeTracker.Entries().Any(entry => entry.Entity is Maple2.Database.Model.Item row && row.ItemId == HeldTop);
            return false;
        };
        PutOn(held, EquipSlot.CL);
        test.FailSave = null;

        Dictionary<int, (long Owner, ItemGroup Group)> rows = Rows();
        Assert.Multiple(() => {
            Assert.That(rows[HeldTop], Is.EqualTo((doll.Uid, ItemGroup.FittingDoll)));
            // The group tells the doll's row from the character's: the test db gives the cube and the character the same id.
            Assert.That(rows[DollTop], Is.EqualTo((test.CharacterId, ItemGroup.Default)));
        });
    }

    // The put-on answered: the held item is on the doll, the item taken off in the inventory, both stored so.
    [Test]
    public void TheItemTakenOffGoesToTheInventory() {
        Start(Gear(DollTop, EquipSlot.CL), Gear(HeldTop, EquipSlot.CL));
        Assert.That(PutOn(Held(), EquipSlot.CL).Select(Convert.ToHexString), Has.None.EqualTo(Error(0xc)));

        Dictionary<int, (long Owner, ItemGroup Group)> rows = Rows();
        Assert.Multiple(() => {
            Assert.That(OnDoll(EquipSlot.CL), Is.EqualTo(HeldTop));
            Assert.That(rows[HeldTop], Is.EqualTo((doll.Uid, ItemGroup.FittingDoll)));
            Assert.That(rows[DollTop], Is.EqualTo((test.CharacterId, ItemGroup.Default)));
            Assert.That(session.Item.Inventory.Find(DollTop), Has.Exactly(1).Items);
        });
    }

    // The save refused: s_fittingdoll_InvalidItem, and the doll, the inventory and the rows are as they were.
    [Test]
    public void FailedSaveChangesNothing() {
        Start(Gear(DollTop, EquipSlot.CL), Gear(HeldTop, EquipSlot.CL));
        Item held = Held();
        Dictionary<int, (long Owner, ItemGroup Group)> rows = Rows();
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.Entity is Maple2.Database.Model.Item);
        Assert.That(PutOn(held, EquipSlot.CL).Select(Convert.ToHexString), Is.EqualTo(new[] { Error(0xc) }));
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(Rows(), Is.EqualTo(rows));
            Assert.That(OnDoll(EquipSlot.CL), Is.EqualTo(DollTop));
            Assert.That(doll.Items[EquipSlot.CL].Group, Is.EqualTo(ItemGroup.FittingDoll));
            Assert.That(doll.Items[EquipSlot.CL].Slot, Is.EqualTo((short) EquipSlot.CL));
            Assert.That(Held().Group, Is.EqualTo(ItemGroup.Default));
            Assert.That(session.Item.Inventory.Find(DollTop), Is.Empty);
        });
    }
}

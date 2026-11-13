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

// FittingDoll 3, the swap: the doll's items and the character's worn items of the doll's kind change places in one
// save. The ids are made up.
public class FittingDollTransformTests {
    private const int DollItem = 50400001;
    private const int DollTop = 11400001;
    private const int WornTop = 11400002;
    private const int DollPants = 11500001;
    private const int WornPants = 11500002;
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

    // A male equipment doll placed in the session's home holding |onDoll|, the character wearing |worn| (gear).
    private void Start(ItemMetadata[] onDoll, ItemMetadata[] worn) {
        test = new TestGameSession();
        foreach (ItemMetadata metadata in worn) {
            test.Store(metadata, group: ItemGroup.Gear, slot: metadata.SlotNames[0]);
        }
        test.Start();
        plot = test.EnterHome();
        plot.Id = PlotId;
        ItemMetadata dollMetadata = TestGameSession.Metadata(DollItem);
        test.Register(dollMetadata);
        PlotCube cube;
        using (GameStorage.Request db = test.Storage.Context()) {
            cube = db.CreateCube(plot, new PlotCube(dollMetadata) { Position = new Vector3B(1, 2, 3), Type = PlotCube.CubeType.Construction })!;
        }
        doll = new FieldFittingDoll(9000, cube, plot, new FunctionCubeMetadata.FittingDollData(Gender.Male, 0));
        foreach (ItemMetadata metadata in onDoll) {
            test.Register(metadata);
            using GameStorage.Request db = test.Storage.Context();
            Item item = db.CreateItem(cube.Id, new Item(metadata) {
                Group = ItemGroup.FittingDoll,
                Slot = (short) metadata.SlotNames[0],
            })!;
            doll.Items[metadata.SlotNames[0]] = item;
        }
        // The test field is made without its constructor, so the doll table is set here.
        FieldInfo field = typeof(FieldManager).GetField("fittingDolls", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var dolls = new ConcurrentDictionary<long, FieldFittingDoll> { [cube.Id] = doll };
        field.SetValue(session.Field, dolls);
        test.TakeSent();
    }

    private List<byte[]> Transform() {
        new FittingDollHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(doll.Uid);
        }));
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.FittingDoll).ToList();
    }

    private static string Answer(long dollUid, int result) {
        var writer = Packet.Of(SendOp.FittingDoll);
        writer.WriteByte(8);
        writer.WriteLong(dollUid);
        writer.WriteInt(result);
        return Convert.ToHexString(writer.ToArray());
    }

    private static string Error(int error) {
        var writer = Packet.Of(SendOp.FittingDoll);
        writer.WriteByte(11);
        writer.WriteInt(error);
        return Convert.ToHexString(writer.ToArray());
    }

    private Dictionary<int, (long Owner, ItemGroup Group, short Slot)> Rows() {
        using var context = new Ms2Context(test.Options);
        return context.Item.AsNoTracking().ToList().ToDictionary(row => row.ItemId, row => (row.OwnerId, row.Group, row.Slot));
    }

    private int Worn(EquipSlot slot) => session.Item.Equips.Gear.TryGetValue(slot, out Item? item) ? item.Id : 0;
    private int OnDoll(EquipSlot slot) => doll.Items.TryGetValue(slot, out Item? item) ? item.Id : 0;

    [Test]
    public void SwapMovesBothSidesAndAnswersDone() {
        Start([Gear(DollTop, EquipSlot.CL, transfer: TransferType.BindOnEquip)], [Gear(WornTop, EquipSlot.CL)]);
        List<byte[]> sent = Transform();

        Assert.That(sent.Select(Convert.ToHexString).Last(), Is.EqualTo(Answer(doll.Uid, 0x17)));
        Dictionary<int, (long Owner, ItemGroup Group, short Slot)> rows = Rows();
        Assert.Multiple(() => {
            Assert.That(Worn(EquipSlot.CL), Is.EqualTo(DollTop));
            Assert.That(OnDoll(EquipSlot.CL), Is.EqualTo(WornTop));
            Assert.That(rows[DollTop], Is.EqualTo((test.CharacterId, ItemGroup.Gear, (short) EquipSlot.CL)));
            Assert.That(rows[WornTop], Is.EqualTo((doll.Uid, ItemGroup.FittingDoll, (short) EquipSlot.CL)));
            // Wearing binds a bind-on-equip item, as an equip does.
            Assert.That(session.Item.Equips.Gear[EquipSlot.CL].Transfer?.Binding?.CharacterId, Is.EqualTo(test.CharacterId));
        });
    }

    // Worn pants that are not swapped (subtype 0) keep their slot: the doll's pants stay on the doll.
    [Test]
    public void ItemOverlappingOneThatStaysIsHeldBack() {
        Start([Gear(DollTop, EquipSlot.CL), Gear(DollPants, EquipSlot.PA)], [Gear(WornTop, EquipSlot.CL), Gear(WornPants, EquipSlot.PA, subType: 0)]);
        List<byte[]> sent = Transform();

        Assert.That(sent.Select(Convert.ToHexString).Last(), Is.EqualTo(Answer(doll.Uid, 0x18)));
        Assert.Multiple(() => {
            Assert.That(Worn(EquipSlot.CL), Is.EqualTo(DollTop));
            Assert.That(Worn(EquipSlot.PA), Is.EqualTo(WornPants));
            Assert.That(OnDoll(EquipSlot.CL), Is.EqualTo(WornTop));
            Assert.That(OnDoll(EquipSlot.PA), Is.EqualTo(DollPants));
        });
    }

    [Test]
    public void NothingToSwapAnswersNothingAndWritesNothing() {
        Start([], [Gear(WornPants, EquipSlot.PA, subType: 0)]);
        Dictionary<int, (long Owner, ItemGroup Group, short Slot)> rows = Rows();
        Assert.That(Transform().Select(Convert.ToHexString), Is.EqualTo(new[] { Answer(doll.Uid, 0x19) }));
        Assert.That(Rows(), Is.EqualTo(rows));
    }

    private void AssertUnchanged(Dictionary<int, (long Owner, ItemGroup Group, short Slot)> rows) {
        Assert.Multiple(() => {
            Assert.That(Rows(), Is.EqualTo(rows));
            Assert.That(Worn(EquipSlot.CL), Is.EqualTo(WornTop));
            Assert.That(OnDoll(EquipSlot.CL), Is.EqualTo(DollTop));
            Assert.That(session.Item.Equips.Gear[EquipSlot.CL].Group, Is.EqualTo(ItemGroup.Gear));
            Assert.That(doll.Items[EquipSlot.CL].Group, Is.EqualTo(ItemGroup.FittingDoll));
            Assert.That(doll.Items[EquipSlot.CL].Transfer?.Binding, Is.Null);
        });
    }

    [Test]
    public void OnlyThePlotOwnerSwaps() {
        Start([Gear(DollTop, EquipSlot.CL)], [Gear(WornTop, EquipSlot.CL)]);
        plot.OwnerId = test.AccountId + 1;
        Dictionary<int, (long Owner, ItemGroup Group, short Slot)> rows = Rows();
        Assert.That(Transform().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(2) }));
        AssertUnchanged(rows);
    }

    // A worn female top cannot go onto the male doll: s_fittingdoll_Invaliditem_male, and nothing moves.
    [Test]
    public void WornItemTheDollCannotWearRefusesTheSwap() {
        Start([Gear(DollTop, EquipSlot.CL)], [Gear(WornTop, EquipSlot.CL, gender: Gender.Female)]);
        Dictionary<int, (long Owner, ItemGroup Group, short Slot)> rows = Rows();
        Assert.That(Transform().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(7) }));
        AssertUnchanged(rows);
    }

    // A doll item the character may not use: s_fittingdoll_Invaliditem_limit.
    [Test]
    public void DollItemTheCharacterCannotUseRefusesTheSwap() {
        Start([Gear(DollTop, EquipSlot.CL, gender: Gender.Female)], [Gear(WornTop, EquipSlot.CL)]);
        Dictionary<int, (long Owner, ItemGroup Group, short Slot)> rows = Rows();
        Assert.That(Transform().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(6) }));
        AssertUnchanged(rows);
    }

    // The database refuses the save: s_fittingdoll_failswap, and both sides are as they were, in memory and in the
    // DB, the binding the swap would have made included.
    [Test]
    public void FailedSaveLeavesBothSidesAsTheyWere() {
        Start([Gear(DollTop, EquipSlot.CL, transfer: TransferType.BindOnEquip)], [Gear(WornTop, EquipSlot.CL)]);
        Dictionary<int, (long Owner, ItemGroup Group, short Slot)> rows = Rows();
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.Entity is Maple2.Database.Model.Item);
        Assert.That(Transform().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(0x14) }));
        test.FailSave = null;
        AssertUnchanged(rows);
    }
}

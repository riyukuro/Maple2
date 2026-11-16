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
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// FittingDoll 10, sent by MyPC before a doll is recalled (client/ms2/mypc.cpp:4118): byte 10, long doll uid. The
// doll's items leave it: each slot is put off on the field, an item of the owner goes to the inventory, an item bound
// to another character of the account becomes that character's row. The ids are made up.
public class FittingDollReturnAllTests {
    private const int DollItem = 50400001;
    private const int Top = 11400001;
    private const int Bottom = 11500001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private FieldFittingDoll doll = null!;
    private Item top = null!;
    private Item bottom = null!;
    private long otherCharacter;

    [SetUp]
    public void Start() {
        test = new TestGameSession();
        test.Start();
        Plot plot = test.EnterHome();
        plot.Id = 7001;
        ItemMetadata dollMetadata = TestGameSession.Metadata(DollItem);
        ItemMetadata topMetadata = TestGameSession.Metadata(Top, EquipSlot.CL);
        ItemMetadata bottomMetadata = TestGameSession.Metadata(Bottom, EquipSlot.PA);
        test.Register(dollMetadata, topMetadata, bottomMetadata);
        PlotCube cube;
        using (GameStorage.Request db = test.Storage.Context()) {
            otherCharacter = db.CreateCharacter(new Character {
                AccountId = session.AccountId,
                Name = Guid.NewGuid().ToString("N")[..12],
                Job = Job.Newbie,
                Mastery = new Mastery(),
                MapId = 2000023,
                ReturnMaps = new Maple2.Tools.LimitedStack<int>(3, 2000023),
            })!.Id;
            cube = db.CreateCube(plot, new PlotCube(dollMetadata) { Position = new Vector3B(1, 2, 3), Type = PlotCube.CubeType.Construction })!;
            top = db.CreateItem(cube.Id, new Item(topMetadata) { Group = ItemGroup.FittingDoll, Slot = (short) EquipSlot.CL })!;
            bottom = db.CreateItem(cube.Id, new Item(bottomMetadata) {
                Group = ItemGroup.FittingDoll,
                Slot = (short) EquipSlot.PA,
                Transfer = new ItemTransfer(binding: new ItemBinding(otherCharacter, "other")),
            })!;
        }
        doll = new FieldFittingDoll(9000, cube, plot, new FunctionCubeMetadata.FittingDollData(Gender.Male, 0));
        doll.Items[EquipSlot.CL] = top;
        doll.Items[EquipSlot.PA] = bottom;
        FieldInfo field = typeof(FieldManager).GetField("fittingDolls", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(session.Field, new ConcurrentDictionary<long, FieldFittingDoll> { [cube.Id] = doll });
        test.TakeSent();
    }

    private List<string> ReturnAll(long dollUid) {
        new FittingDollHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.WriteLong(dollUid);
        }));
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.FittingDoll).Select(Convert.ToHexString).ToList();
    }

    private (long Owner, ItemGroup Group)? Row(long uid) {
        using var context = new Ms2Context(test.Options);
        Maple2.Database.Model.Item? row = context.Item.AsNoTracking().FirstOrDefault(item => item.Id == uid);
        return row == null ? null : (row.OwnerId, row.Group);
    }

    private static string Error(int error) {
        var writer = Packet.Of(SendOp.FittingDoll);
        writer.WriteByte(11);
        writer.WriteInt(error);
        return Convert.ToHexString(writer.ToArray());
    }

    [Test]
    public void Owner_TakesEveryItemOff() {
        List<string> sent = ReturnAll(doll.Uid);

        Assert.Multiple(() => {
            Assert.That(sent, Is.EquivalentTo(new[] {
                Convert.ToHexString(FittingDollPacket.PutOff(doll, EquipSlot.CL).ToArray()),
                Convert.ToHexString(FittingDollPacket.PutOff(doll, EquipSlot.PA).ToArray()),
            }));
            Assert.That(doll.Items, Is.Empty);
            Assert.That(Row(top.Uid), Is.EqualTo((session.CharacterId, ItemGroup.Default)));
            Assert.That(session.Item.Inventory.Get(top.Uid), Is.Not.Null);
            Assert.That(Row(bottom.Uid), Is.EqualTo((otherCharacter, ItemGroup.Default)));
            Assert.That(session.Item.Inventory.Get(bottom.Uid), Is.Null);
        });
    }

    [Test]
    public void NotThePlotOwner_IsRefused() {
        doll.Plot.OwnerId = session.AccountId + 1;

        List<string> sent = ReturnAll(doll.Uid);

        Assert.Multiple(() => {
            Assert.That(sent, Is.EqualTo(new[] { Error(2) }));
            Assert.That(doll.Items, Has.Count.EqualTo(2));
            Assert.That(Row(top.Uid), Is.EqualTo((doll.Uid, ItemGroup.FittingDoll)));
        });
    }

    [Test]
    public void UnknownDoll_IsRefused() {
        Assert.That(ReturnAll(doll.Uid + 1), Is.EqualTo(new[] { Error(0xd) }));
        Assert.That(doll.Items, Has.Count.EqualTo(2));
    }
}

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

// FittingDoll 9: the plot owner destroys a doll item bound to a character outside the account. The ids are made up.
public class FittingDollDestroyTests {
    private const int DollItem = 50400001;
    private const int Top = 11400001;
    private const long PlotId = 7001;
    private const long OtherAccount = 990_001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Plot plot = null!;
    private FieldFittingDoll doll = null!;
    private Item top = null!;

    private long NewCharacter(long accountId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.CreateCharacter(new Character {
            AccountId = accountId,
            Name = Guid.NewGuid().ToString("N")[..12],
            Job = Job.Newbie,
            Mastery = new Mastery(),
            MapId = 2000023,
            ReturnMaps = new Maple2.Tools.LimitedStack<int>(3, 2000023),
        })!.Id;
    }

    // A doll in the session's home holding a top bound to |bound| (0: unbound), with one gem per |gems| entry bound to
    // that character (0: an unbound gem).
    private void Start(Func<long> bound, Func<long[]>? gems = null) {
        test = new TestGameSession();
        test.Start();
        plot = test.EnterHome();
        plot.Id = PlotId;
        ItemMetadata dollMetadata = TestGameSession.Metadata(DollItem);
        ItemMetadata topMetadata = TestGameSession.Metadata(Top, EquipSlot.CL);
        test.Register(dollMetadata, topMetadata);
        long character = bound();
        long[] gemOwners = gems?.Invoke() ?? [];
        PlotCube cube;
        using (GameStorage.Request db = test.Storage.Context()) {
            cube = db.CreateCube(plot, new PlotCube(dollMetadata) { Position = new Vector3B(1, 2, 3), Type = PlotCube.CubeType.Construction })!;
            top = db.CreateItem(cube.Id, new Item(topMetadata) {
                Group = ItemGroup.FittingDoll,
                Slot = (short) EquipSlot.CL,
                Transfer = new ItemTransfer(binding: character == 0 ? null : new ItemBinding(character, "bound")),
                Socket = new ItemSocket(3, gemOwners.Select(owner =>
                    (ItemGemstone?) new ItemGemstone(40000001, owner == 0 ? null : new ItemBinding(owner, "gem"), new ItemStats())).ToArray()),
            })!;
        }
        doll = new FieldFittingDoll(9000, cube, plot, new FunctionCubeMetadata.FittingDollData(Gender.Male, 0));
        doll.Items[EquipSlot.CL] = top;
        // The test field is made without its constructor, so the doll table is set here.
        FieldInfo field = typeof(FieldManager).GetField("fittingDolls", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(session.Field, new ConcurrentDictionary<long, FieldFittingDoll> { [cube.Id] = doll });
        test.TakeSent();
    }

    private List<string> Destroy(long itemUid) {
        new FittingDollHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(9);
            writer.WriteLong(doll.Uid);
            writer.WriteLong(itemUid);
        }));
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.FittingDoll).Select(Convert.ToHexString).ToList();
    }

    private static string Error(int error) {
        var writer = Packet.Of(SendOp.FittingDoll);
        writer.WriteByte(11);
        writer.WriteInt(error);
        return Convert.ToHexString(writer.ToArray());
    }

    private string PutOff() => Convert.ToHexString(FittingDollPacket.PutOff(doll, EquipSlot.CL).ToArray());

    private (long Owner, ItemGroup Group)? Row(long uid) {
        using var context = new Ms2Context(test.Options);
        Maple2.Database.Model.Item? row = context.Item.AsNoTracking().FirstOrDefault(item => item.Id == uid);
        return row == null ? null : (row.OwnerId, row.Group);
    }

    private void AssertKept() {
        Assert.Multiple(() => {
            Assert.That(Row(top.Uid), Is.EqualTo((doll.Uid, ItemGroup.FittingDoll)));
            Assert.That(doll.Items.ContainsKey(EquipSlot.CL), Is.True);
        });
    }

    [Test]
    public void ItemBoundToAnotherAccountsCharacterIsDestroyed() {
        Start(() => NewCharacter(OtherAccount));
        List<string> sent = Destroy(top.Uid);

        Assert.Multiple(() => {
            Assert.That(sent, Is.EqualTo(new[] { PutOff() }));
            Assert.That(Row(top.Uid), Is.Null);
            Assert.That(doll.Items, Is.Empty);
        });
    }

    // A deleted character's row is gone: it is outside the account too.
    [Test]
    public void ItemBoundToACharacterThatNoLongerExistsIsDestroyed() {
        Start(() => 880_000_001);
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { PutOff() }));
        Assert.That(Row(top.Uid), Is.Null);
    }

    // Another character of the account gets the item back with a take-off or a recall, so it is not destroyed.
    [Test]
    public void ItemBoundToACharacterOfTheAccountIsKept() {
        Start(() => NewCharacter(test.AccountId));
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { Error(0x12) }));
        AssertKept();
    }

    [Test]
    public void ItemBoundToTheSenderIsKept() {
        Start(() => test.CharacterId);
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { Error(0x12) }));
        AssertKept();
    }

    [Test]
    public void UnboundItemIsKept() {
        Start(() => 0);
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { Error(0xc) }));
        AssertKept();
    }

    [Test]
    public void OnlyThePlotOwnerDestroys() {
        Start(() => NewCharacter(OtherAccount));
        plot.OwnerId = test.AccountId + 1;
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { Error(2) }));
        AssertKept();
    }

    [Test]
    public void UnknownItemIsRefused() {
        Start(() => NewCharacter(OtherAccount));
        Assert.That(Destroy(top.Uid + 100), Is.EqualTo(new[] { Error(0xc) }));
        AssertKept();
    }

    [Test]
    public void RemovedDollIsRefused() {
        Start(() => NewCharacter(OtherAccount));
        doll.Removed = true;
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { Error(0xd) }));
        Assert.That(Row(top.Uid), Is.EqualTo((doll.Uid, ItemGroup.FittingDoll)));
    }

    // The same home on another channel took the item off first: nothing is deleted, the stale entry is dropped.
    [Test]
    public void ItemThatLeftTheDollIsNotDeleted() {
        Start(() => NewCharacter(OtherAccount));
        using (var context = new Ms2Context(test.Options)) {
            Maple2.Database.Model.Item row = context.Item.Single(item => item.Id == top.Uid);
            row.OwnerId = test.CharacterId;
            row.Group = ItemGroup.Default;
            context.SaveChanges();
        }
        List<string> sent = Destroy(top.Uid);

        Assert.Multiple(() => {
            Assert.That(sent, Is.EqualTo(new[] { PutOff(), Error(0xc) }));
            Assert.That(Row(top.Uid), Is.EqualTo((test.CharacterId, ItemGroup.Default)));
            Assert.That(doll.Items, Is.Empty);
        });
    }

    // Reason 5: the item itself is free, a gem in it is bound outside the account.
    [Test]
    public void UnboundItemWithAGemBoundToAnotherAccountsCharacterIsDestroyed() {
        Start(() => 0, () => [NewCharacter(OtherAccount)]);
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { PutOff() }));
        Assert.That(Row(top.Uid), Is.Null);
    }

    [Test]
    public void ItemBoundToTheSenderWithAGemBoundToAnotherAccountsCharacterIsDestroyed() {
        Start(() => test.CharacterId, () => [0, NewCharacter(OtherAccount)]);
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { PutOff() }));
        Assert.That(Row(top.Uid), Is.Null);
    }

    // Reason 2: the gem's character is in the account.
    [Test]
    public void GemBoundToACharacterOfTheAccountIsKept() {
        Start(() => 0, () => [NewCharacter(test.AccountId)]);
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { Error(0x12) }));
        AssertKept();
    }

    // The client names the last bound gem (CItemSocket::GetGemBindOwner): here the sender's own, so no destroy is offered.
    [Test]
    public void LastBoundGemDecidesTheOwner() {
        Start(() => 0, () => [NewCharacter(OtherAccount), test.CharacterId]);
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { Error(0x12) }));
        AssertKept();
    }

    [Test]
    public void UnboundGemsAreNoBinding() {
        Start(() => 0, () => [0, 0]);
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { Error(0xc) }));
        AssertKept();
    }

    // The item binding comes first: a character of the account keeps it whatever its gems.
    [Test]
    public void ItemBindingInTheAccountWinsOverAForeignGem() {
        Start(() => NewCharacter(test.AccountId), () => [NewCharacter(OtherAccount)]);
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { Error(0x12) }));
        AssertKept();
    }

    // A character waiting for its deletion is still in the account.
    [Test]
    public void ItemBoundToAPendingDeleteCharacterOfTheAccountIsKept() {
        Start(() => {
            long character = NewCharacter(test.AccountId);
            using var context = new Ms2Context(test.Options);
            context.Character.Single(row => row.Id == character).DeleteTime = DateTime.Now.AddDays(1);
            context.SaveChanges();
            return character;
        });
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { Error(0x12) }));
        AssertKept();
    }

    [Test]
    public void SecondDestroyOfTheSameItemIsRefused() {
        Start(() => NewCharacter(OtherAccount));
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { PutOff() }));
        Assert.That(Destroy(top.Uid), Is.EqualTo(new[] { Error(0xc) }));
        Assert.That(Row(top.Uid), Is.Null);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// LimitBreakHandler, MesoMarketHandler (the window's load) and ItemLockHandler, as KMS2 answers them.
public class LimitBreakMarketLockHandlerTests {
    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private TestGameSession test = null!;
    private Item weapon = null!;

    private static ItemMetadata Material(int id, ItemTag tag) {
        ItemMetadata metadata = TestGameSession.Metadata(id, type: 2, slotMax: 30000);
        return metadata with { Property = metadata.Property with { Tag = tag } };
    }

    // A weapon at enchant 15 and limit break 0, the four catalysts of the first step and some crystal fragments.
    private GameSession LimitBreakLogin(int fragments, long meso) {
        test = new TestGameSession();
        var option = new UnlimitedEnchantOptionTable.Option(new Dictionary<BasicAttribute, int>(), new Dictionary<BasicAttribute, float>(),
            new Dictionary<SpecialAttribute, int>(), new Dictionary<SpecialAttribute, float>());
        test.SetServerTable(new UnlimitedEnchantOptionTable(Enumerable.Range(0, 100)
            .ToDictionary(type => type, _ => new Dictionary<int, UnlimitedEnchantOptionTable.Option> { [1] = option })));
        ItemMetadata metadata = TestGameSession.Metadata(15000001, EquipSlot.RH, type: 1, slotMax: 1);
        weapon = test.Store(metadata with { Property = metadata.Property with { LimitBreakMaxLevel = 60 } });
        test.Store(Material(40100001, ItemTag.ChaosOnix), amount: 60);
        test.Store(Material(40100002, ItemTag.Onix), amount: 3600);
        test.Store(Material(40100003, ItemTag.PrismShard), amount: 70);
        test.Store(Material(40100004, ItemTag.PrismStone), amount: 5);
        if (fragments > 0) {
            test.Store(Material(40100005, ItemTag.CrystalPiece), amount: fragments);
        }
        GameSession session = test.Start();
        session.Item.Inventory.Get(weapon.Uid)!.Enchant = new ItemEnchant(15);
        session.Currency.Meso = meso;
        test.TakeSent();
        return session;
    }

    private void LimitBreak(GameSession session, byte command) {
        new LimitBreakHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteLong(weapon.Uid);
        }));
    }

    private static int Held(GameSession session, ItemTag tag) => session.Item.Inventory.Find(tag).Sum(item => item.Amount);

    // The preview: the uid, then five ingredients (int 0, client tag, amount). The fifth is the fee in crystal
    // fragments (client tag 0x60), a thousandth of the meso fee.
    [Test]
    public void LimitBreak_PreviewNamesTheFeeInFragments() {
        GameSession session = LimitBreakLogin(fragments: 0, meso: 0);

        LimitBreak(session, 0);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.LimitBreak }));
        string expected = "00" + Convert.ToHexString(BitConverter.GetBytes(weapon.Uid)) + "05"
                          + "00000000" + "62000000" + "36000000"
                          + "00000000" + "61000000" + "B60D0000"
                          + "00000000" + "D2000000" + "40000000"
                          + "00000000" + "D3000000" + "04000000"
                          + "00000000" + "60000000" + "93040000";
        Assert.That(Convert.ToHexString(sent[0])[4..], Does.StartWith(expected));
    }

    // The upgrade: the meso first, then the materials, then the result. Owned fragments are taken and only the
    // lacking ones cost 1000 meso each.
    [TestCase(0, 1171000)]
    [TestCase(171, 1000000)]
    [TestCase(2000, 0)]
    public void LimitBreak_UpgradeTakesTheMesoThenTheMaterials(int fragments, long mesoFee) {
        GameSession session = LimitBreakLogin(fragments, meso: 2000000);
        LimitBreak(session, 0);
        test.TakeSent();

        LimitBreak(session, 1);

        List<SendOp> ops = test.TakeSentOps().ToList();
        if (mesoFee > 0) {
            Assert.That(ops[0], Is.EqualTo(SendOp.Meso));
        } else {
            Assert.That(ops, Has.No.Member(SendOp.Meso));
        }
        Assert.That(ops.IndexOf(SendOp.LimitBreak), Is.GreaterThan(ops.LastIndexOf(SendOp.ItemInventory)));
        Assert.That(session.Currency.Meso, Is.EqualTo(2000000 - mesoFee));
        Assert.That(Held(session, ItemTag.CrystalPiece), Is.EqualTo(Math.Max(0, fragments - 1171)));
        Assert.That(Held(session, ItemTag.ChaosOnix), Is.EqualTo(6));
        Assert.That(Held(session, ItemTag.Onix), Is.EqualTo(90));
        Assert.That(Held(session, ItemTag.PrismShard), Is.EqualTo(6));
        Assert.That(Held(session, ItemTag.PrismStone), Is.EqualTo(1));
        Assert.That(session.Item.Inventory.Get(weapon.Uid)!.LimitBreak!.Level, Is.EqualTo(1));
    }

    [Test]
    public void LimitBreak_WithoutTheMesoTakesNothing() {
        GameSession session = LimitBreakLogin(fragments: 171, meso: 999999);
        LimitBreak(session, 0);
        test.TakeSent();

        LimitBreak(session, 1);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.LimitBreak }));
        Assert.That(session.Currency.Meso, Is.EqualTo(999999));
        Assert.That(Held(session, ItemTag.CrystalPiece), Is.EqualTo(171));
        Assert.That(Held(session, ItemTag.ChaosOnix), Is.EqualTo(60));
        Assert.That(session.Item.Inventory.Get(weapon.Uid)!.LimitBreak!.Level, Is.EqualTo(0));
    }

    // The meso market window: the limits, the counts, the character's black market listings, the own list.
    [Test]
    public void MesoMarket_LoadSendsTheLimitsAsKms2() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();

        new MesoMarketHandler { ServerTableMetadata = test.ServerTableMetadata }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.MesoMarket, SendOp.MesoMarket, SendOp.BlackMarket, SendOp.MesoMarket }));
        // tax 0.05, range 0.2, the average price, then 5, 5, 10, 2, 100, 50, 3000
        string limits = Convert.ToHexString(sent[0])[4..];
        Assert.That(limits[..18], Is.EqualTo("01CDCC4C3DCDCC4C3E"));
        Assert.That(limits[34..], Is.EqualTo("05000000050000000A000000020000006400000032000000B80B0000"));
        Assert.That(Convert.ToHexString(sent[1])[4..], Is.EqualTo("020000000000000000"));
        Assert.That(Convert.ToHexString(sent[2])[4..], Is.EqualTo("0100000000"));
        Assert.That(Convert.ToHexString(sent[3])[4..], Is.EqualTo("0400000000"));
    }

    // The lock dialog: opening it has no answer, an added item is answered with its uid and place, and the
    // commit lists the locked items.
    [Test]
    public void ItemLock_StageThenCommit() {
        test = new TestGameSession();
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, type: 1, slotMax: 1));
        GameSession session = test.Start();
        test.TakeSent();
        // The harness session is made without its field initializers.
        typeof(GameSession).GetField(nameof(GameSession.ItemLockStaging))!.SetValue(session, new long[18]);
        var handler = new ItemLockHandler { ServerTableMetadata = test.ServerTableMetadata };

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0)));
        Assert.That(test.TakeSent(), Is.Empty);

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteBool(false);
            writer.WriteLong(cap.Uid);
        }));
        string uid = Convert.ToHexString(BitConverter.GetBytes(cap.Uid));
        Assert.That(test.TakeSent().Select(packet => Convert.ToHexString(packet)[4..]), Is.EqualTo(new[] { "01" + uid + "0000" }));

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteBool(false);
        }));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ItemLock }));
        Assert.That(Convert.ToHexString(sent[0])[4..24], Is.EqualTo("0301" + uid));
        Assert.That(session.Item.Inventory.Get(cap.Uid)!.IsLocked, Is.True);
    }

    // Two upgrade requests on one preview: the second takes nothing and the level stays.
    [Test]
    public void LimitBreak_TwiceOnOnePreviewPaysOnce() {
        GameSession session = LimitBreakLogin(fragments: 171, meso: 2000000);
        LimitBreak(session, 0);
        LimitBreak(session, 1);
        test.TakeSent();

        LimitBreak(session, 1);

        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Has.No.Member(SendOp.ItemInventory).And.No.Member(SendOp.Meso));
            Assert.That(session.Currency.Meso, Is.EqualTo(1000000));
            Assert.That(Held(session, ItemTag.ChaosOnix), Is.EqualTo(6));
            Assert.That(Held(session, ItemTag.Onix), Is.EqualTo(90));
            Assert.That(session.Item.Inventory.Get(weapon.Uid)!.LimitBreak!.Level, Is.EqualTo(1));
        });
    }

    // A locked item is not staged for another lock, and a commit with nothing staged changes nothing.
    [Test]
    public void ItemLock_CommitTwiceLocksOnce() {
        test = new TestGameSession();
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, type: 1, slotMax: 1));
        GameSession session = test.Start();
        typeof(GameSession).GetField(nameof(GameSession.ItemLockStaging))!.SetValue(session, new long[18]);
        var handler = new ItemLockHandler { ServerTableMetadata = test.ServerTableMetadata };
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0)));
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteBool(false);
            writer.WriteLong(cap.Uid);
        }));
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteBool(false);
        }));
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteBool(false);
        }));
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteBool(false);
            writer.WriteLong(cap.Uid);
        }));

        Assert.That(session.Item.Inventory.Get(cap.Uid)!.IsLocked, Is.True);
        Assert.That(session.ItemLockStaging, Is.All.EqualTo(0L));
        Assert.That(test.Errors, Is.Empty);
    }
}

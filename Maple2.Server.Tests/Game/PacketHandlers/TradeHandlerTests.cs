using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A trade between two sessions, from the accepted request on (the request itself looks the other player up in the
// field, which the harness does not have). The answers are in the order KMS2 sends them.
public class TradeHandlerTests {
    private TestGameSession giverTest = null!;
    private TestGameSession takerTest = null!;
    private readonly TradeHandler handler = new();
    private Item item = null!;
    private GameSession giver => giverTest.Session;
    private GameSession taker => takerTest.Session;

    [SetUp]
    public void StartTrade() => StartTrade(TestGameSession.Metadata(20000001, type: 2));

    private void StartTrade(ItemMetadata metadata, bool takerBagFull = false, int amount = 3) {
        giverTest = new TestGameSession();
        // One database, as on a server: the item rows the trade moves are in it.
        takerTest = new TestGameSession(giverTest.Options);
        item = giverTest.Store(metadata, amount: amount);
        takerTest.Register(metadata);
        if (takerBagFull) {
            // One slot in the taker's Misc tab, taken by another item.
            takerTest.Store(TestGameSession.Metadata(20000009));
            takerTest.SetServerTable(takerTest.ServerTableMetadata.ConstantsTable with { bagSlotTabMiscCount = [(short) 1, 0] });
        }
        // The request's time-out is set far off: with the table's 0 seconds the timer can end the request before
        // the taker's answer below, and the trade then never starts.
        foreach (TestGameSession test in new[] { giverTest, takerTest }) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { TradeMinMeso = 100, TradeMaxMeso = 1000000, TradeRequestDuration = 30 });
        }
        giverTest.Start();
        takerTest.Start();
        taker.Currency.Meso = 5000;
        var trade = new TradeManager(giver, taker);
        giver.Trade = trade;
        taker.Trade = trade;
        trade.Acknowledge(taker);
        trade.Accept(taker);
        giverTest.TakeSent();
        takerTest.TakeSent();
    }

    private void Send(GameSession session, byte command, Action<Maple2.PacketLib.Tools.ByteWriter>? write = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            write?.Invoke(writer);
        }));
    }

    private void Offer() {
        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(3);
            writer.WriteInt(0);
        });
        Send(taker, 10, writer => writer.WriteLong(1000));
    }

    // A new request from giver to taker. Its time-out is set far off, so it does not end the request mid-test.
    private TradeManager Request() {
        giverTest.SetServerTable(giverTest.ServerTableMetadata.ConstantsTable with { TradeRequestDuration = 30 });
        var request = new TradeManager(giver, taker);
        giver.Trade = request;
        taker.Trade = request;
        giverTest.TakeSent();
        takerTest.TakeSent();
        return request;
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static string[] Modes(List<byte[]> sent) => sent.Select(packet => $"{TestGameSession.Op(packet)} {packet[2]}").ToArray();

    // A locked item, and one whose unlock wait is not over, stays in the bag: the client does not offer it.
    [Test]
    public void Offer_OfALockedItemIsDropped() {
        giver.Item.Inventory.Get(item.Uid)!.IsLocked = true;
        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(3);
            writer.WriteInt(0);
        });
        giver.Item.Inventory.Get(item.Uid)!.IsLocked = false;
        giver.Item.Inventory.Get(item.Uid)!.UnlockTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(3);
            writer.WriteInt(0);
        });

        Assert.That(giverTest.TakeSent(), Is.Empty);
        Assert.That(takerTest.TakeSent(), Is.Empty);
        Assert.That(giver.Item.Inventory.Get(item.Uid)!.Amount, Is.EqualTo(3));
    }

    // An offered item leaves the bag, then enters the window; offered mesos leave the player, then enter the window.
    [Test]
    public void Offer_Order() {
        Offer();

        Assert.That(Modes(giverTest.TakeSent()), Is.EqualTo(new[] { "ItemInventory 1", "Trade 8", "Trade 10" }));
        List<byte[]> sent = takerTest.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Trade, SendOp.Meso, SendOp.Trade }));
        Assert.That(Convert.ToHexString(sent[2]), Is.EqualTo(Hex(TradePacket.SetMesos(true, 1000))));
        Assert.That(taker.Currency.Meso, Is.EqualTo(4000));
    }

    // The finish: the giver's window loses the item; the taker gets the item as an own offer, its removal, then the
    // bag's add; the mesos come after the items; EndTrade is last. A side that gets no mesos gets no Meso.
    [Test]
    public void Complete_HandsTheOffersOver() {
        Offer();
        Send(giver, 11);
        Send(taker, 11);
        Send(giver, 13);
        giverTest.TakeSent();
        takerTest.TakeSent();

        Send(taker, 13);

        List<byte[]> toGiver = giverTest.TakeSent();
        Assert.That(toGiver.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Trade, SendOp.Trade, SendOp.Meso, SendOp.Trade }));
        Assert.That(new[] { toGiver[0], toGiver[1], toGiver[3] }.Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(TradePacket.Complete(false)),
            Hex(TradePacket.RemoveItem(true, 0, item.Uid)),
            Hex(TradePacket.EndTrade(true)),
        }));
        List<byte[]> toTaker = takerTest.TakeSent();
        Assert.That(Modes(toTaker).Take(4), Is.EqualTo(new[] { "Trade 13", "Trade 8", "Trade 9", "ItemInventory 0" }));
        Assert.That(Convert.ToHexString(toTaker[2]), Is.EqualTo(Hex(TradePacket.RemoveItem(true, 0, item.Uid))));
        Assert.That(toTaker.Select(TestGameSession.Op), Does.Not.Contain(SendOp.Meso));
        Assert.That(Convert.ToHexString(toTaker[^1]), Is.EqualTo(Hex(TradePacket.EndTrade(true))));

        Assert.That(giver.Currency.Meso, Is.EqualTo(1000));
        Assert.That(taker.Currency.Meso, Is.EqualTo(4000));
        Assert.That(giver.Item.Inventory.Find(20000001), Is.Empty);
        Assert.That(taker.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(3));
        Assert.That(giver.Trade, Is.Null);
        Assert.That(taker.Trade, Is.Null);
    }

    // The receiver pays the fee on the mesos it gets, lowered by its own reduce_meso_trade_fee (total / 1000), the
    // amount the client shows under the other offer: 10% of 1000 is 100, a quarter off leaves 75.
    [Test]
    public void Complete_TakesTheReceiversFee() {
        foreach (TestGameSession test in new[] { giverTest, takerTest }) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { TradeFeePercent = 10 });
        }
        giver.Stats.Values[SpecialAttribute.ReduceMesoTradeFee].AddTotal(250);
        Offer();
        Send(giver, 11);
        Send(taker, 11);
        Send(giver, 13);
        Send(taker, 13);

        Assert.That(giver.Currency.Meso, Is.EqualTo(925));
        Assert.That(taker.Currency.Meso, Is.EqualTo(4000));
    }

    // The handed item's row is the taker's once the trade ends. Before, it stayed the giver's until the taker's next
    // save, so the giver loaded the item again on a relog in between.
    [Test]
    public void Complete_MovesTheRowToTheTaker() {
        Offer();
        Send(giver, 11);
        Send(taker, 11);
        Send(giver, 13);
        Send(taker, 13);

        using GameStorage.Request db = giverTest.Storage.Context();
        int Rows(long characterId) => db.GetItemGroups(characterId, ItemGroup.Default).TryGetValue(ItemGroup.Default, out List<Item>? rows)
            ? rows.Where(row => row.Id == 20000001).Sum(row => row.Amount) : 0;
        Assert.Multiple(() => {
            Assert.That(Rows(taker.CharacterId), Is.EqualTo(3));
            Assert.That(Rows(giver.CharacterId), Is.Zero);
            Assert.That(giverTest.Errors.Concat(takerTest.Errors), Is.Empty);
        });
    }

    // Part of a stack offered and taken back from the window is written back at once: the stack's row holds the
    // whole amount again. Before, the return was kept in memory, and the split piece's row (owner 0) and the lowered
    // stack were what a crash before the next save left.
    [Test]
    public void RemoveItem_WritesThePieceBack() {
        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(2);
            writer.WriteInt(0);
        });
        // The window's slot names the piece; the uid is only echoed back.
        Send(giver, 9, writer => {
            writer.WriteLong(0);
            writer.WriteInt(0);
        });

        using GameStorage.Request db = giverTest.Storage.Context();
        int held = db.GetItemGroups(giver.CharacterId, ItemGroup.Default).TryGetValue(ItemGroup.Default, out List<Item>? rows)
            ? rows.Where(row => row.Id == 20000001).Sum(row => row.Amount) : 0;
        Assert.Multiple(() => {
            Assert.That(giver.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(3));
            Assert.That(held, Is.EqualTo(3));
            Assert.That(giverTest.Errors, Is.Empty);
        });
    }

    // A slot past the short range is refused: it named slot 0 here (65536 wraps to 0) and took the offered item back
    // to the bag, while both windows were told of slot 65536, so the taker still saw the item as offered.
    [TestCase(65536)]
    [TestCase(-65536)]
    public void RemoveItem_ASlotPastTheShortRangeIsRefused(int tradeSlot) {
        Offer();
        giverTest.TakeSent();
        takerTest.TakeSent();
        Send(giver, 9, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(tradeSlot);
        });

        Assert.Multiple(() => {
            Assert.That(giver.Item.Inventory.Get(item.Uid), Is.Null);
            Assert.That(takerTest.TakeSent(), Is.Empty);
        });
    }

    // Both windows are told the slot's own uid: before, the client's uid was echoed, so a wrong one dropped
    // another offer from the windows while this slot's item came back to the bag.
    [Test]
    public void RemoveItem_EchoesTheSlotsUid() {
        Offer();
        giverTest.TakeSent();
        takerTest.TakeSent();
        Send(giver, 9, writer => {
            writer.WriteLong(item.Uid + 1);
            writer.WriteInt(0);
        });

        byte[][] sent = takerTest.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Trade).ToArray();
        Assert.That(sent, Has.Length.EqualTo(1));
        Assert.That(BitConverter.ToInt64(sent[0], 2 + 1 + 1 + 4), Is.EqualTo(item.Uid));
    }

    // The rows of |characterId| holding item 20000001, as the next login loads them.
    private int Held(long characterId) {
        using GameStorage.Request db = giverTest.Storage.Context();
        return db.GetItemGroups(characterId, ItemGroup.Default).TryGetValue(ItemGroup.Default, out List<Item>? rows)
            ? rows.Where(row => row.Id == 20000001).Sum(row => row.Amount) : 0;
    }

    // Part of a stack in the window is still the giver's row until the trade ends: a crash then (no hand-over, no
    // cancel) loads the whole stack again. Before, the piece's row was owner 0, so the crash lost it.
    [Test]
    public void OfferedPiece_StaysTheGiversRowUntilTheEnd() {
        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(2);
            writer.WriteInt(0);
        });
        int inTrade = Held(giver.CharacterId);

        Send(giver, 11);
        Send(taker, 11);
        Send(giver, 13);
        Send(taker, 13);

        Assert.Multiple(() => {
            Assert.That(inTrade, Is.EqualTo(3));
            Assert.That(Held(giver.CharacterId), Is.EqualTo(1));
            Assert.That(Held(taker.CharacterId), Is.EqualTo(2));
            Assert.That(giverTest.Errors.Concat(takerTest.Errors), Is.Empty);
        });
    }

    // A second stack of the same item offered after the first is stacked onto it in the window: its row is written
    // to owner 0 with the first's new amount, so the giver keeps nothing of it after the trade and a crash during the
    // trade loads both amounts once. Before, its row stayed the giver's and the giver loaded it again after the trade.
    [Test]
    public void StackedOffer_IsNotLeftWithTheGiver() {
        Item second = giverTest.Store(TestGameSession.Metadata(20000001, type: 2), amount: 2);
        // Its own bag slot, as a login loads two rows of one item (no stacking). Slot 7 is past the window's 5, so
        // the window stacks it (an item whose bag slot is free in the window keeps that slot there).
        second.Slot = 7;
        var tabs = (Dictionary<InventoryType, ItemCollection>) typeof(InventoryManager)
            .GetField("tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(giver.Item.Inventory)!;
        Assert.That(tabs[second.Inventory].Add(second), Is.Not.Empty);
        foreach (long uid in new[] { item.Uid, second.Uid }) {
            Send(giver, 8, writer => {
                writer.WriteLong(uid);
                writer.WriteInt(uid == item.Uid ? 3 : 2);
                writer.WriteInt(0);
            });
        }
        int inTrade = Held(giver.CharacterId);

        Send(giver, 11);
        Send(taker, 11);
        Send(giver, 13);
        Send(taker, 13);

        Assert.Multiple(() => {
            Assert.That(inTrade, Is.EqualTo(5));
            Assert.That(Held(giver.CharacterId), Is.Zero);
            Assert.That(Held(taker.CharacterId), Is.EqualTo(5));
            Assert.That(taker.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(5));
            Assert.That(giverTest.Errors.Concat(takerTest.Errors), Is.Empty);
        });
    }

    // The stack's write fails: the window takes the stack back and the second stack goes back to the bag, so the
    // window, the bag and the rows agree (3 offered, 2 held).
    [Test]
    public void StackedOffer_FailedWriteReturnsIt() {
        Item second = giverTest.Store(TestGameSession.Metadata(20000001, type: 2), amount: 2);
        second.Slot = 7;
        var tabs = (Dictionary<InventoryType, ItemCollection>) typeof(InventoryManager)
            .GetField("tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(giver.Item.Inventory)!;
        Assert.That(tabs[second.Inventory].Add(second), Is.Not.Empty);
        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(3);
            writer.WriteInt(0);
        });
        int failures = 1;
        giverTest.FailSave = _ => failures-- > 0;
        try {
            Send(giver, 8, writer => {
                writer.WriteLong(second.Uid);
                writer.WriteInt(2);
                writer.WriteInt(0);
            });
        } finally {
            giverTest.FailSave = null;
        }

        Assert.Multiple(() => {
            Assert.That(item.Amount, Is.EqualTo(3));
            Assert.That(giver.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(2));
            Assert.That(Held(giver.CharacterId), Is.EqualTo(5));
        });
        Send(giver, 7);
        Assert.That(giver.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(5));
    }

    // A cancel: each own item leaves the window and enters the bag, the mesos come back, then EndTrade (failed).
    [Test]
    public void Cancel_ReturnsTheOffers() {
        Offer();
        giverTest.TakeSent();
        takerTest.TakeSent();

        Send(giver, 7);

        List<byte[]> toGiver = giverTest.TakeSent();
        Assert.That(Modes(toGiver), Is.EqualTo(new[] { "Trade 9", "ItemInventory 0", "Trade 6" }));
        Assert.That(Convert.ToHexString(toGiver[2]), Is.EqualTo(Hex(TradePacket.EndTrade(false))));
        List<byte[]> toTaker = takerTest.TakeSent();
        Assert.That(toTaker.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Meso, SendOp.Trade }));
        Assert.That(taker.Currency.Meso, Is.EqualTo(5000));
        Assert.That(giver.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(3));
    }

    // A finish refused for a full bag: the full side gets the bag's notice, the other side trade error 8, then the
    // trade ends as cancelled. The dialog's confirm click greyed its Cancel button and nothing enables it again
    // (uitradedialogex.gfx), so the trade end is the answer that leaves no window waiting: each offer goes back to
    // its owner, nothing moves, and both players are out of the trade.
    [Test]
    public void Complete_WithAFullBagEndsTheTrade() {
        StartTrade(TestGameSession.Metadata(20000001), takerBagFull: true);
        Assert.That(item.Inventory, Is.EqualTo(InventoryType.Misc));
        Assert.That(taker.Item.Inventory.FreeSlots(item.Inventory), Is.EqualTo(0));
        Offer();
        Send(giver, 11);
        Send(taker, 11);
        Send(giver, 13);
        giverTest.TakeSent();
        takerTest.TakeSent();

        Send(taker, 13);

        List<byte[]> toTaker = takerTest.TakeSent();
        Assert.That(toTaker.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Trade, SendOp.ItemInventory, SendOp.Meso, SendOp.Trade }));
        Assert.That(Convert.ToHexString(toTaker[1]), Is.EqualTo(Convert.ToHexString(
            ItemInventoryPacket.Error(Maple2.Model.Error.ItemInventoryError.s_err_inventory).ToArray())));
        Assert.That(Convert.ToHexString(toTaker[^1]), Is.EqualTo(Hex(TradePacket.EndTrade(false))));
        List<byte[]> toGiver = giverTest.TakeSent();
        Assert.That(Convert.ToHexString(toGiver[1]), Is.EqualTo(Hex(TradePacket.Error(Maple2.Model.Error.TradeError.s_trade_error_slotcount))));
        Assert.That(Modes(toGiver), Is.EqualTo(new[] { "Trade 13", "Trade 1", "Trade 9", "ItemInventory 0", "Trade 6" }));
        Assert.That(Convert.ToHexString(toGiver[^1]), Is.EqualTo(Hex(TradePacket.EndTrade(false))));

        Assert.That(giver.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(3));
        Assert.That(taker.Item.Inventory.Find(20000001), Is.Empty);
        Assert.That(taker.Currency.Meso, Is.EqualTo(5000));
        Assert.That(giver.Trade, Is.Null);
        Assert.That(taker.Trade, Is.Null);
    }

    // A refused offer: the notice (or nothing), and neither the bag nor the mesos nor the other window change.
    // 6000 is more than the player has, 2000000 more than a trade takes, 50 less than it takes (TradeMinMeso), -5 is
    // not an amount; after the player's own lock every change is refused as latched.
    [TestCase(6000L, false, "s_trade_error_meso")]
    [TestCase(2000000L, false, "s_trade_error_invalid_meso")]
    [TestCase(50L, false, "s_trade_error_invalid_meso")]
    [TestCase(-5L, false, null)]
    [TestCase(1000L, true, "s_trade_error_latched")]
    public void SetMesos_Refused(long amount, bool locked, string? error) {
        if (locked) {
            Send(taker, 11);
            giverTest.TakeSent();
            takerTest.TakeSent();
        }

        Send(taker, 10, writer => writer.WriteLong(amount));

        string[] expected = error == null ? [] : [Hex(TradePacket.Error(Enum.Parse<Maple2.Model.Error.TradeError>(error)))];
        Assert.That(takerTest.TakeSent().Select(Convert.ToHexString), Is.EqualTo(expected));
        Assert.That(giverTest.TakeSent(), Is.Empty);
        Assert.That(taker.Currency.Meso, Is.EqualTo(5000));
        Send(taker, 7);
        Assert.That(taker.Currency.Meso, Is.EqualTo(5000));
    }

    // A refused item: more than the stack, no amount, an item the player does not have, or any item after the
    // player's own lock. The stack stays whole in the bag and the other player is shown nothing.
    [TestCase(4, false, false)]
    [TestCase(0, false, false)]
    [TestCase(-1, false, false)]
    [TestCase(3, true, false)]
    [TestCase(3, false, true)]
    public void AddItem_Refused(int amount, bool unknown, bool locked) {
        if (locked) {
            Send(giver, 11);
            giverTest.TakeSent();
            takerTest.TakeSent();
        }

        Send(giver, 8, writer => {
            writer.WriteLong(unknown ? item.Uid + 50 : item.Uid);
            writer.WriteInt(amount);
            writer.WriteInt(0);
        });

        // More than the stack is answered by the bag (the count is wrong); the lock by the trade.
        string[] expected = locked ? [Hex(TradePacket.Error(Maple2.Model.Error.TradeError.s_trade_error_latched))]
            : amount == 4 ? [Hex(ItemInventoryPacket.Error(Maple2.Model.Error.ItemInventoryError.s_item_err_invalid_count))] : [];
        Assert.That(giverTest.TakeSent().Select(Convert.ToHexString), Is.EqualTo(expected));
        Assert.That(takerTest.TakeSent(), Is.Empty);
        Assert.That(giver.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(3));
        Send(giver, 7);
        Assert.That(giver.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(3));
    }

    // A black-market-only item comes with the trade bit and the client offers it (FUN_140635590 reads only the
    // rule, never the transfer type or the trade rarity): it goes into the trade at its trade rarity too (Blue
    // Paint 20000208, rarity 4, TradeMaxRarity 4, was kept in the bag with no answer).
    [TestCase(1)]
    [TestCase(2)]
    public void AddItem_BlackMarketOnly(int tradeMaxRarity) {
        ItemMetadata metadata = TestGameSession.Metadata(20000001, type: 2);
        StartTrade(metadata with { Limit = metadata.Limit with { TransferType = TransferType.BlackMarketOnly, TradeMaxRarity = tradeMaxRarity } });
        Assert.That(giver.Item.Inventory.Get(item.Uid)!.Transfer!.Flag, Is.EqualTo(TransferFlag.Trade));

        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(3);
            writer.WriteInt(0);
        });

        Assert.That(giver.Item.Inventory.Get(item.Uid), Is.Null);
        Assert.That(takerTest.TakeSent(), Is.Not.Empty);
    }

    // The pet that is out stays with its owner: its manager keeps the pet bag (rows owned by the pet's uid) and
    // writes it when the pet is put away, so after a trade both players held its items. Put away, it goes.
    [TestCase(true)]
    [TestCase(false)]
    public void AddItem_TheSummonedPetStays(bool summoned) {
        const int petId = 60000001;
        ItemMetadata petData = TestGameSession.Metadata(petId, type: 11, slotMax: 1);
        StartTrade(petData with { Property = petData.Property with { PetId = petId } }, amount: 1);
        Item pet = giver.Item.Inventory.Get(item.Uid)!;
        if (summoned) {
            // The trade reads the summoned item only; the field pet itself needs the npc and the navigation data a
            // test session has none of.
            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var fieldPet = (Maple2.Server.Game.Model.FieldPet) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Maple2.Server.Game.Model.FieldPet));
            typeof(Maple2.Server.Game.Model.FieldPet).GetField(nameof(Maple2.Server.Game.Model.FieldPet.Pet), any)!.SetValue(fieldPet, pet);
            var manager = (PetManager) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PetManager));
            typeof(PetManager).GetField("session", any)!.SetValue(manager, giver);
            typeof(PetManager).GetField("pet", any)!.SetValue(manager, fieldPet);
            giver.Pet = manager;
        }

        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(1);
            writer.WriteInt(0);
        });
        giver.Pet = null;

        Assert.Multiple(() => {
            Assert.That(giver.Item.Inventory.Get(item.Uid) != null, Is.EqualTo(summoned));
            Assert.That(takerTest.TakeSent(), summoned ? Is.Empty : Is.Not.Empty);
        });
    }

    // The score being played stays in the bag, as the client refuses it (s_trade_tradelimit_playing_score); once
    // the play has stopped, it goes.
    [TestCase(true)]
    [TestCase(false)]
    public void AddItem_TheScoreBeingPlayedStays(bool playing) {
        if (playing) {
            // The trade reads the score only; a field instrument needs a field the test session has none of.
            var instrument = (Maple2.Server.Game.Model.FieldInstrument) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Maple2.Server.Game.Model.FieldInstrument));
            instrument.Score = giver.Item.Inventory.Get(item.Uid);
            giver.Instrument = instrument;
        }

        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(3);
            writer.WriteInt(0);
        });
        giver.Instrument = null;

        Assert.Multiple(() => {
            Assert.That(giver.Item.Inventory.Get(item.Uid)?.Amount, playing ? Is.EqualTo(3) : Is.Null);
            Assert.That(takerTest.TakeSent(), playing ? Is.Empty : Is.Not.Empty);
        });
    }

    // KMS2 order: a player who has locked is unlocked (mode 12) before the other player's change is shown.
    [Test]
    public void Offer_UnlocksTheOtherSideFirst() {
        Send(taker, 11);
        giverTest.TakeSent();
        takerTest.TakeSent();

        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(3);
            writer.WriteInt(0);
        });

        Assert.That(Modes(takerTest.TakeSent()), Is.EqualTo(new[] { "Trade 12", "Trade 8" }));
        Assert.That(Modes(giverTest.TakeSent()), Is.EqualTo(new[] { "ItemInventory 1", "Trade 12", "Trade 8" }));

        Send(giver, 11);
        giverTest.TakeSent();
        takerTest.TakeSent();
        Send(taker, 10, writer => writer.WriteLong(1000));

        List<byte[]> toGiver = giverTest.TakeSent();
        Assert.That(Modes(toGiver), Is.EqualTo(new[] { "Trade 12", "Trade 10" }));
        Assert.That(Convert.ToHexString(toGiver[0]), Is.EqualTo(Hex(TradePacket.UnFinalize(true))));
    }

    // A cancel: the own mesos come back before the own items.
    [Test]
    public void Cancel_ReturnsTheMesosFirst() {
        giver.Currency.Meso = 700;
        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(3);
            writer.WriteInt(0);
        });
        Send(giver, 10, writer => writer.WriteLong(700));
        giverTest.TakeSent();

        Send(taker, 7);

        Assert.That(Modes(giverTest.TakeSent()), Is.EqualTo(new[] { "Meso 0", "Trade 9", "ItemInventory 0", "Trade 6" }).Using<string, string>((got, want) => got.Split(' ')[0] == "Meso" ? want.StartsWith("Meso") : got == want));
        Assert.That(giver.Currency.Meso, Is.EqualTo(700));
    }

    // An offer racing the other player's cancel: whichever comes first, the item is in the giver's bag and the
    // mesos with the taker afterwards, and nothing is left in a window of an ended trade.
    [Test]
    public void Offer_RacingACancelLosesNothing() {
        for (int round = 0; round < 60; round++) {
            StartTrade();
            giver.Currency.Meso = 800;

            System.Threading.Tasks.Parallel.Invoke(
                () => {
                    Send(giver, 8, writer => {
                        writer.WriteLong(item.Uid);
                        writer.WriteInt(2);
                        writer.WriteInt(0);
                    });
                    Send(giver, 10, writer => writer.WriteLong(800));
                },
                () => Send(taker, round % 2 == 0 ? (byte) 10 : (byte) 7, writer => writer.WriteLong(1000)),
                () => Send(taker, 7));

            Assert.That(giver.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(3), $"round {round}");
            Assert.That(giver.Currency.Meso, Is.EqualTo(800), $"round {round}");
            Assert.That(taker.Currency.Meso, Is.EqualTo(5000), $"round {round}");
            Assert.That(giver.Trade, Is.Null, $"round {round}");
            Assert.That(giverTest.Errors.Concat(takerTest.Errors), Is.Empty, $"round {round}");
        }
    }

    // A third player is not part of a request: it cannot acknowledge it, and a cancel from it (even holding the
    // trade, as an acknowledge naming a trader once gave it) leaves the two players' trade and offers alone.
    [Test]
    public void ThirdPlayer_CannotAcknowledgeOrCancel() {
        var thirdTest = new TestGameSession();
        thirdTest.Start();
        GameSession third = thirdTest.Session;
        Offer();
        TradeManager trade = giver.Trade!;
        giverTest.TakeSent();
        takerTest.TakeSent();

        Assert.That(trade.IsTrader(third), Is.False);
        Assert.That(trade.Acknowledge(third), Is.False);
        third.Trade = trade;
        Send(third, 7);

        Assert.That(third.Trade, Is.Null);
        Assert.That(giver.Trade, Is.SameAs(trade));
        Assert.That(taker.Trade, Is.SameAs(trade));
        Assert.That(giverTest.TakeSent(), Is.Empty);
        Assert.That(takerTest.TakeSent(), Is.Empty);
        Assert.That(taker.Currency.Meso, Is.EqualTo(4000));
        Assert.That(giver.Item.Inventory.Find(20000001), Is.Empty);

        // The request of a new trade: only the player asked can acknowledge it.
        Send(giver, 7);
        TradeManager request = Request();
        Assert.That(request.Acknowledge(third), Is.False);
        Assert.That(request.Acknowledge(giver), Is.False);
        Assert.That(giverTest.TakeSent(), Is.Empty);
        Assert.That(request.Acknowledge(taker), Is.True);
        Assert.That(request.Acknowledge(taker), Is.False);
        Assert.That(giverTest.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(TradePacket.Acknowledge()) }));
        request.Dispose();
    }

    // An accept after the request ended (cancelled or timed out) starts nothing.
    [Test]
    public void Accept_AfterTheRequestEndedStartsNothing() {
        Send(giver, 7);
        TradeManager request = Request();
        Assert.That(request.Acknowledge(taker), Is.True);
        request.Dispose();
        giverTest.TakeSent();
        takerTest.TakeSent();

        request.Accept(taker);
        request.Decline(taker);

        Assert.That(giverTest.TakeSent(), Is.Empty);
        Assert.That(takerTest.TakeSent(), Is.Empty);
        Assert.That(giver.Trade, Is.Null);
        Assert.That(taker.Trade, Is.Null);
    }

    // An accept that waits for the trade's lock while the request is ended under it starts nothing: the state
    // is read after the wait, not before it.
    [Test]
    public void Accept_WaitingWhileTheRequestEndsStartsNothing() {
        Send(giver, 7);
        TradeManager request = Request();
        Assert.That(request.Acknowledge(taker), Is.True);
        giverTest.TakeSent();
        takerTest.TakeSent();
        object mutex = typeof(TradeManager).GetField("mutex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(request)!;

        var accept = new System.Threading.Thread(() => request.Accept(taker));
        lock (mutex) {
            accept.Start();
            for (int i = 0; i < 2000 && (accept.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == 0; i++) {
                System.Threading.Thread.Sleep(1);
            }
            Assert.That(accept.ThreadState & System.Threading.ThreadState.WaitSleepJoin, Is.Not.EqualTo((System.Threading.ThreadState) 0));
            request.Dispose();
        }
        Assert.That(accept.Join(5000), Is.True);

        Assert.That(giverTest.TakeSent(), Is.Empty);
        Assert.That(takerTest.TakeSent(), Is.Empty);
        Assert.That(giver.Trade, Is.Null);
        Assert.That(taker.Trade, Is.Null);
    }

    // Acknowledge gives the trade to the player asked, and the end of a request takes it only from players who
    // still hold it: one who is in another trade by then keeps that one.
    [Test]
    public void EndOfARequest_LeavesANewerTradeAlone() {
        Send(giver, 7);
        TradeManager old = Request();
        taker.Trade = null;
        Assert.That(old.Acknowledge(taker), Is.True);
        Assert.That(taker.Trade, Is.SameAs(old));

        TradeManager newer = Request();
        old.Dispose();
        Assert.That(giver.Trade, Is.SameAs(newer));
        Assert.That(taker.Trade, Is.SameAs(newer));

        // A request that is over gives the trade to no one.
        taker.Trade = null;
        Assert.That(old.Acknowledge(taker), Is.False);
        Assert.That(taker.Trade, Is.Null);
        newer.Dispose();
        Assert.That(giver.Trade, Is.Null);
    }

    // An accept and the end of the request at the same moment: the trade either never starts, or starts and is
    // then ended for both; a started trade is never left on a request that was dropped.
    [Test]
    public void Accept_RacingTheEndOfTheRequest() {
        Send(giver, 7);
        string start = Hex(TradePacket.StartTrade(taker.CharacterId));
        string end = Hex(TradePacket.EndTrade(false));
        for (int round = 0; round < 40; round++) {
            TradeManager request = Request();
            Assert.That(request.Acknowledge(taker), Is.True);
            giverTest.TakeSent();

            System.Threading.Tasks.Parallel.Invoke(() => request.Accept(taker), request.Dispose);

            string[] toGiver = giverTest.TakeSent().Select(Convert.ToHexString).ToArray();
            Assert.That(toGiver, Is.EqualTo(Array.Empty<string>()).Or.EqualTo(new[] { start, end }), $"round {round}");
            Assert.That(takerTest.TakeSent().Count, Is.EqualTo(toGiver.Length), $"round {round}");
            Assert.That(giver.Trade, Is.Null, $"round {round}");
            Assert.That(taker.Trade, Is.Null, $"round {round}");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The npc remake dialog as the client drives it: an earring put in the slot (ChangeAttributes 4) is answered with
// the cost; a change (0) leaves a choice pending, which the dialog's reopening (6, kind 1) shows again and the keep
// (2) puts on the earring; a keep of another item, or a reopening with the earring gone, is answered as such. The ids
// are made up.
public class ChangeAttributesFlowTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int EarringId = 11200962, RandomId = 9962;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Item item = null!;

    private static ItemMetadata Tagged(int id, ItemTag tag) {
        ItemMetadata metadata = TestGameSession.Metadata(id, type: 4, slotMax: 9999);
        return metadata with { Property = metadata.Property with { Tag = tag } };
    }

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        var entries = new ItemOption.Entry[] {
            new(BasicAttribute.Strength, Values: new ItemOption.Range<int>(5, 5)),
            new(BasicAttribute.Health, Values: new ItemOption.Range<int>(5, 5)),
        };
        test.SetTable(new ItemOptionRandomTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>> {
            [RandomId] = new Dictionary<int, ItemOption> { [4] = new ItemOption(1f, new ItemOption.Range<int>(2, 2), entries) },
        }));
        var variation = new ItemEquipVariationTable(new Dictionary<BasicAttribute, ItemEquipVariationTable.Set<int>[]>(),
            new Dictionary<BasicAttribute, ItemEquipVariationTable.Set<float>[]>(),
            new Dictionary<SpecialAttribute, ItemEquipVariationTable.Set<int>[]>(),
            new Dictionary<SpecialAttribute, ItemEquipVariationTable.Set<float>[]>());
        foreach (string name in new[] { "accVariationTable", "petVariationTable" }) {
            typeof(Maple2.Database.Storage.TableMetadataStorage).GetField(name, Any)!
                .SetValue(test.TableMetadata, new Lazy<ItemEquipVariationTable>(() => variation));
        }
        ItemMetadata earring = TestGameSession.Metadata(EarringId, slot: EquipSlot.EA, type: 1, slotMax: 1);
        Item stored = test.Store(earring with {
            Limit = earring.Limit with { Level = 50 },
            Option = new ItemMetadataOption(0, default, RandomId, default, 0, default, 0, 0),
        }, rarity: 4);
        test.Store(Tagged(40100961, ItemTag.CrystalPiece), 9999);
        test.Store(Tagged(40100962, ItemTag.RedCrystal), 9999);
        test.Store(Tagged(40100963, ItemTag.MetaCell), 9999);
        test.Store(Tagged(40100964, ItemTag.GreenCrystal), 9999);
        test.Start();
        session.Currency.Meso = 100_000_000;
        item = session.Item.Inventory.Get(stored.Uid)!;
        item.Stats = new ItemStats();
        item.Stats[ItemStats.Type.Random].Basic[BasicAttribute.Strength] = new BasicOption(1);
        item.Stats[ItemStats.Type.Random].Basic[BasicAttribute.Health] = new BasicOption(1);
        test.TakeSent();
    }

    private void Handle(Action<ByteWriter> write) {
        new ChangeAttributesHandler { ItemStatsCalc = new ItemStatsCalculator { TableMetadata = test.TableMetadata } }
            .Handle(session, TestGameSession.Packet(write));
    }

    private void Change() {
        Handle(writer => {
            writer.WriteByte(0);
            writer.WriteLong(item.Uid);
            writer.WriteLong(0);
            writer.WriteByte(0);
        });
        Assert.That(session.ChangeAttributesItem?.Uid, Is.EqualTo(item.Uid), "the change is pending");
        test.TakeSent();
    }

    private void Reopen() => Handle(writer => {
        writer.WriteByte(6);
        writer.WriteByte(1);
    });

    private List<string> Sent() => test.TakeSent().Select(Convert.ToHexString).ToList();

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void AnEarringPutInTheSlot_IsAnsweredWithTheCost() {
        Handle(writer => {
            writer.WriteByte(4);
            writer.WriteLong(item.Uid);
        });
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ChangeAttributesPacket.StageItem(item.Uid, ChangeAttributesHandler.GetIngredients(item))) }));
    }

    [Test]
    public void AnItemNotHeldPutInTheSlot_IsRefused() {
        Handle(writer => {
            writer.WriteByte(4);
            writer.WriteLong(item.Uid + 1000);
        });
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ChangeAttributesPacket.Error(ChangeAttributesError.s_itemremake_error_server_not_in_inven)) }));
    }

    [Test]
    public void KeepingTheChange_PutsItsOptionsOnTheEarring() {
        Change();
        Item pending = session.ChangeAttributesItem!;
        Handle(writer => {
            writer.WriteByte(2);
            writer.WriteLong(item.Uid);
        });
        Assert.Multiple(() => {
            Assert.That(session.ChangeAttributesItem, Is.Null);
            Assert.That(item.Stats![ItemStats.Type.Random].Basic[BasicAttribute.Strength].Value, Is.EqualTo(5));
            Assert.That(item.Stats[ItemStats.Type.Random], Is.SameAs(pending.Stats![ItemStats.Type.Random]));
            Assert.That(Sent(), Does.Contain(Hex(ChangeAttributesPacket.SelectItem(item))));
        });
    }

    [Test]
    public void KeepingAnotherItem_IsRefusedAndTheChangeStaysPending() {
        Change();
        Handle(writer => {
            writer.WriteByte(2);
            writer.WriteLong(item.Uid + 1000);
        });
        Assert.Multiple(() => {
            Assert.That(session.ChangeAttributesItem?.Uid, Is.EqualTo(item.Uid));
            Assert.That(item.Stats![ItemStats.Type.Random].Basic[BasicAttribute.Strength].Value, Is.EqualTo(1));
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(ChangeAttributesPacket.Error(ChangeAttributesError.s_itemremake_error_server_fail_apply_option)) }));
        });
    }

    [Test]
    public void Reopening_ShowsThePendingChange() {
        Change();
        Reopen();
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ChangeAttributesPacket.PendingItem(session.ChangeAttributesItem!)) }));
    }

    [Test]
    public void ReopeningWithNothingPending_SaysSo() {
        Reopen();
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(ChangeAttributesPacket.NoPending()) }));
    }

    // The pet tab (kind 2) does not resume the earring's change.
    [Test]
    public void ThePetTab_DoesNotResumeTheEarringsChange() {
        Change();
        Handle(writer => {
            writer.WriteByte(6);
            writer.WriteByte(2);
        });
        Assert.Multiple(() => {
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(ChangeAttributesPacket.NoPending()) }));
            Assert.That(session.ChangeAttributesItem?.Uid, Is.EqualTo(item.Uid));
        });
    }

    [Test]
    public void ReopeningWithTheEarringGone_DropsThePendingChange() {
        Change();
        Assert.That(session.Item.Inventory.Remove(item.Uid, out _), Is.True);
        test.TakeSent();
        Reopen();
        Assert.Multiple(() => {
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(ChangeAttributesPacket.PendingItemGone(EarringId)) }));
            Assert.That(session.ChangeAttributesItem, Is.Null);
        });
    }
}

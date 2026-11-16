using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A remake scroll used as the client sends it (ChangeAttributesScroll 1, the scroll, the item, no lock; then 3, keep):
// the change takes one scroll and answers with the preview, the item is unchanged until it is kept, and keeping puts the
// previewed options on it. The ids are made up.
public class ChangeAttributesScrollFlowTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int EarringId = 11200961, ScrollId = 20300961, RandomId = 9961, RemakeId = 5;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Item item = null!, scroll = null!;

    private void Start(bool rollAttribute) {
        test = new TestGameSession();
        var entries = new ItemOption.Entry[] {
            new(BasicAttribute.Strength, Values: new ItemOption.Range<int>(1, 9)),
            new(BasicAttribute.Health, Values: new ItemOption.Range<int>(1, 9)),
        };
        test.SetTable(new ItemOptionRandomTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>> {
            [RandomId] = new Dictionary<int, ItemOption> { [4] = new ItemOption(1f, new ItemOption.Range<int>(2, 2), entries) },
        }));
        var variation = new ItemEquipVariationTable(new Dictionary<BasicAttribute, ItemEquipVariationTable.Set<int>[]>(),
            new Dictionary<BasicAttribute, ItemEquipVariationTable.Set<float>[]>(),
            new Dictionary<SpecialAttribute, ItemEquipVariationTable.Set<int>[]>(),
            new Dictionary<SpecialAttribute, ItemEquipVariationTable.Set<float>[]>());
        foreach (string name in new[] { "accVariationTable", "petVariationTable" }) {
            typeof(TableMetadataStorage).GetField(name, Any)!.SetValue(test.TableMetadata, new Lazy<ItemEquipVariationTable>(() => variation));
        }

        ItemMetadata earring = TestGameSession.Metadata(EarringId, slot: EquipSlot.EA, type: 1, slotMax: 1);
        earring = earring with {
            Limit = earring.Limit with { Level = 50 },
            Option = new ItemMetadataOption(0, default, RandomId, default, 0, default, 0, 0),
        };
        Item stored = test.Store(earring, rarity: 4);
        Item storedScroll = test.Store(TestGameSession.Metadata(ScrollId, type: 4,
            function: new ItemMetadataFunction(ItemFunction.ItemRemakeScroll, "ItemRemakeScroll", $"{RemakeId}", false)), amount: 3);
        test.SetTable(new ItemRemakeScrollTable(new Dictionary<int, ItemRemakeScrollMetadata> {
            [RemakeId] = new(MinLevel: 50, MaxLevel: 50, ItemTypes: [stored.Type.Type], Rarities: [4], RollAttribute: rollAttribute,
                RollValueType: RollValueType.Normal, OnlyPet: false),
        }));

        test.Start();
        item = session.Item.Inventory.Get(stored.Uid)!;
        scroll = session.Item.Inventory.Get(storedScroll.Uid)!;
        item.Stats = new ItemStats();
        item.Stats[ItemStats.Type.Random].Basic[BasicAttribute.Strength] = new BasicOption(1);
        item.Stats[ItemStats.Type.Random].Basic[BasicAttribute.Health] = new BasicOption(1);
        test.TakeSent();
    }

    private void Handle(Action<Maple2.PacketLib.Tools.ByteWriter> write) {
        new ChangeAttributesScrollHandler {
            TableMetadata = test.TableMetadata,
            ItemStatsCalc = new ItemStatsCalculator { TableMetadata = test.TableMetadata },
        }.Handle(session, TestGameSession.Packet(write));
    }

    private void Change(long scrollUid) => Handle(writer => {
        writer.WriteByte(1);
        writer.WriteLong(scrollUid);
        writer.WriteLong(item.Uid);
        writer.WriteLong(0);
        writer.WriteBool(false);
        writer.WriteByte(0);
    });

    private void Select() => Handle(writer => {
        writer.WriteByte(3);
        writer.WriteLong(item.Uid);
    });

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // A fixed scroll keeps the options and rolls their values; a random one rolls the options too.
    [TestCase(false)]
    [TestCase(true)]
    public void AChange_TakesAScrollAndPreviewsWithoutTouchingTheItem(bool rollAttribute) {
        Start(rollAttribute);
        Change(scroll.Uid);

        Item? held = session.ChangeAttributesScrollItem;
        Assert.Multiple(() => {
            Assert.That(held, Is.Not.Null.And.Not.SameAs(item));
            Assert.That(held!.Uid, Is.EqualTo(item.Uid));
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(Hex(ChangeAttributesScrollPacket.PreviewItem(held))));
            Assert.That(scroll.Amount, Is.EqualTo(2));
            Assert.That(item.Stats![ItemStats.Type.Random].Basic[BasicAttribute.Strength].Value, Is.EqualTo(1));
            Assert.That(held.Stats![ItemStats.Type.Random].Basic.Keys,
                rollAttribute ? Is.SubsetOf(new[] { BasicAttribute.Strength, BasicAttribute.Health })
                    : Is.EquivalentTo(new[] { BasicAttribute.Strength, BasicAttribute.Health }));
        });
    }

    [Test]
    public void KeepingThePreview_PutsItsOptionsOnTheItem() {
        Start(rollAttribute: false);
        Change(scroll.Uid);
        Item held = session.ChangeAttributesScrollItem!;
        test.TakeSent();

        Select();

        Assert.Multiple(() => {
            Assert.That(item.Stats![ItemStats.Type.Random].Basic.ToDictionary(pair => pair.Key, pair => pair.Value.Value),
                Is.EquivalentTo(held.Stats![ItemStats.Type.Random].Basic.ToDictionary(pair => pair.Key, pair => pair.Value.Value)));
            Assert.That(session.ChangeAttributesScrollItem, Is.Null);
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(Hex(ChangeAttributesScrollPacket.SelectItem(item))));
        });
    }

    // A scroll the bag does not hold is refused with 10 and nothing is held.
    [Test]
    public void AScrollNotHeld_IsRefused() {
        Start(rollAttribute: false);
        Change(scroll.Uid + 1000);
        Assert.Multiple(() => {
            Assert.That(session.ChangeAttributesScrollItem, Is.Null);
            Assert.That(scroll.Amount, Is.EqualTo(3));
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Hex(ChangeAttributesScrollPacket.Error(Maple2.Model.Error.ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll)),
            }));
        });
    }
}

using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The item dialog (UIItemRemakeOptionDialog, ChangeAttributes) and the remake scroll dialog (UIItemRemakeScrollDialog,
// ChangeAttributesScroll) each hold their own result to keep. Before, both used one pending result in the session,
// so a scroll's result could be kept through the item dialog (without its scroll rules) or resumed by it, and the
// other way round.
public class ChangeAttributesPendingTests {
    private TestGameSession test = null!;

    private (GameSession Session, Item Item, Item Result) Start() {
        test = new TestGameSession();
        Item stored = test.Store(TestGameSession.Metadata(11200002, slot: EquipSlot.RH, type: 1, slotMax: 1));
        GameSession session = test.Start();
        Item item = session.Item.Inventory.Get(stored.Uid)!;
        item.Stats = new ItemStats();
        item.Stats[ItemStats.Type.Random].Basic[BasicAttribute.Strength] = new BasicOption(1);
        Item result = item.Clone();
        result.Stats![ItemStats.Type.Random].Basic[BasicAttribute.Strength] = new BasicOption(9);
        test.TakeSent();
        return (session, item, result);
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void ScrollResult_IsNotKeptOrResumedByTheItemDialog() {
        (GameSession session, Item item, Item result) = Start();
        ChangeAttributesScrollHandler.Hold(session, result);
        var handler = new ChangeAttributesHandler { ItemStatsCalc = null! };

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(6); // OpenDialog
            writer.WriteByte(1); // the item dialog
        }));
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2); // Select
            writer.WriteLong(item.Uid);
        }));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Hex(ChangeAttributesPacket.NoPending()),
                Hex(ChangeAttributesPacket.Error(ChangeAttributesError.s_itemremake_error_server_fail_apply_option)),
            }));
            Assert.That(item.Stats![ItemStats.Type.Random].Basic[BasicAttribute.Strength].Value, Is.EqualTo(1));
            Assert.That(session.ChangeAttributesScrollItem, Is.SameAs(result));
        });
    }

    // The scroll dialog offers option locks only with OptionBlock01 on: a change with a lock (a kept option or the
    // locked flag) is refused before anything else with it off. With it on the same request goes on to the scroll
    // check (no such scroll here).
    [TestCase(1, false, false, ExpectedResult = ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_remake)]
    [TestCase(0, true, false, ExpectedResult = ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_remake)]
    [TestCase(1, false, true, ExpectedResult = ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll)]
    [TestCase(0, false, false, ExpectedResult = ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll)]
    public ChangeAttributesScrollError ScrollLocks_NeedOptionBlock(int keptOptions, bool lockedFlag, bool on) {
        (GameSession session, Item item, _) = Start();
        if (!on) {
            test.SetFeaturesOff("OptionBlock01");
        }

        new ChangeAttributesScrollHandler { TableMetadata = null!, ItemStatsCalc = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1); // Change
            writer.WriteLong(99); // no such scroll
            writer.WriteLong(item.Uid);
            writer.WriteLong(0);
            writer.WriteBool(lockedFlag);
            writer.WriteByte((byte) keptOptions);
            for (int i = 0; i < keptOptions; i++) {
                writer.WriteBool(true); // a special option
                writer.WriteShort(5);
            }
        }));

        byte[] sent = test.TakeSent().Single();
        return (ChangeAttributesScrollError) BitConverter.ToInt32(sent, sent.Length - 4);
    }

    [Test]
    public void ItemDialogResult_IsNotKeptByTheScrollDialog() {
        (GameSession session, Item item, Item result) = Start();
        session.ChangeAttributesItem = result;

        new ChangeAttributesScrollHandler { TableMetadata = null!, ItemStatsCalc = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3); // Select
            writer.WriteLong(item.Uid);
        }));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Hex(ChangeAttributesScrollPacket.Error(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_apply_before_option)),
            }));
            Assert.That(item.Stats![ItemStats.Type.Random].Basic[BasicAttribute.Strength].Value, Is.EqualTo(1));
            Assert.That(session.ChangeAttributesItem, Is.SameAs(result));
        });
    }
}

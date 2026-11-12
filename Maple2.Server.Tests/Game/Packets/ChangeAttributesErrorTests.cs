using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class ChangeAttributesErrorTests {
    // Client UIItemRemakeOptionDialog::OnRemakeError (and the pet page's): kind 5, a byte, an int code. The codes
    // with a string of their own are 1, 2, 3/4, 5, 6/7, 9 and 10; any other prints the default string.
    [TestCase(ChangeAttributesError.s_itemremake_error_server_not_in_inven, 1)]
    [TestCase(ChangeAttributesError.s_itemremake_error_server_impossible, 2)]
    [TestCase(ChangeAttributesError.s_itemremake_error_server_null_status, 3)]
    [TestCase(ChangeAttributesError.s_itemremake_error_server_lack_price, 5)]
    [TestCase(ChangeAttributesError.s_itemremake_error_server_fail_apply_option, 6)]
    [TestCase(ChangeAttributesError.s_itemremake_error_server_fail_lack_lock_consume_item, 9)]
    [TestCase(ChangeAttributesError.s_content_shutdown_notice, 10)]
    [TestCase(ChangeAttributesError.s_itemremake_error_server_default, 255)]
    public void ItemDialogCodes(ChangeAttributesError error, int code) {
        ByteWriter writer = ChangeAttributesPacket.Error(error);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ChangeAttributes));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadByte(), Is.EqualTo(0)); // 0: the dialog leaves its waiting state
        Assert.That(reader.ReadInt(), Is.EqualTo(code));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 4));
    }

    // Client UIItemRemakeScrollDialog::OnRemakeError: kind 4, a byte, an int code; code 0 shows nothing.
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target, 1)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_data, 2)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat, 3)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_slot, 4)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_rank, 5)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_level, 6)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_property, 7)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item, 8)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll, 10)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll_data, 11)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_remake, 12)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_apply_before_option, 14)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_server_fail_consume_scroll, 15)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_error_server_fail_lack_lock_consume_item, 16)]
    [TestCase(ChangeAttributesScrollError.s_content_shutdown_notice, 17)]
    [TestCase(ChangeAttributesScrollError.s_itemremake_scroll_error_server_default, 255)]
    public void ScrollDialogCodes(ChangeAttributesScrollError error, int code) {
        ByteWriter writer = ChangeAttributesScrollPacket.Error(error);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ChangeAttributesScroll));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(code));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 4));
    }
}

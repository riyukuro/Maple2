using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// SendOp 0x00A5, UIPcBangDialog::OnPacket (client/ms2/uipcbangdialog.cpp), and 0x00B5, the PC bang grade of a field
// player (SceneStateGame, sets the player's PC bang byte that PC bang items check).
public static class PcBangPacket {
    private enum Command : byte {
        Info = 0,
        Played = 1,
        Reward = 2,
        Status = 3,
        Notice = 5,
    }

    // OnPacketNotice: 1 s_pcbang_msg_no_pcbang (0xad3), 4 the tutorial field refusal (0xad2).
    public enum NoticeCode : byte {
        NotPcBang = 1,
        Tutorial = 4,
    }

    // OnPacketInfo (read only with feature 0x3c on): grade, start tick, play time (ms), the claimed bits (one byte),
    // the buff shown, the play bonus rows (PlayBonusItemGift) and the gifts (ItemGiftPcBang), then whether to open
    // the dialog. The client counts the time played as (server tick - start) + play time while the grade is above 0.
    public static ByteWriter Info(int grade, int startTick, long playMs, int claimed, int buffId,
                                  IReadOnlyList<PcBangTable.PlayBonus> rows, ICollection<PcBangTable.Gift> gifts, bool show) {
        var pWriter = Packet.Of(SendOp.PcBangBonus);
        pWriter.WriteByte((byte) Command.Info);
        WriteStatus(pWriter, grade, startTick, playMs, claimed);
        pWriter.WriteInt(buffId);
        pWriter.WriteInt(rows.Count);
        foreach (PcBangTable.PlayBonus row in rows) {
            // PlayBonusItemGift::vfunc_6: ItemGiftPcBang, then m_requiredTime (ms) and m_3c. The row's gift id is 0.
            WriteItemEntity(pWriter, row.ItemId, row.Rarity, row.Count);
            pWriter.WriteInt(0);
            pWriter.WriteInt((int) PcBangRules.RequiredMs(row));
            pWriter.WriteInt(0);
        }
        pWriter.WriteInt(gifts.Count);
        foreach (PcBangTable.Gift gift in gifts) {
            WriteItemEntity(pWriter, gift.ItemId, gift.Rarity, gift.Count);
            pWriter.WriteInt(gift.Id);
        }
        pWriter.WriteBool(show);
        return pWriter;
    }

    // OnPacketPlayed: the row claimed (its sound) and the claimed bits.
    public static ByteWriter Played(int index, int claimed) {
        var pWriter = Packet.Of(SendOp.PcBangBonus);
        pWriter.WriteByte((byte) Command.Played);
        pWriter.WriteByte((byte) index);
        pWriter.WriteByte((byte) claimed);
        return pWriter;
    }

    // OnPacketReward: the gift received; the client shows its item in a box.
    public static ByteWriter Reward(int giftId) {
        var pWriter = Packet.Of(SendOp.PcBangBonus);
        pWriter.WriteByte((byte) Command.Reward);
        pWriter.WriteInt(giftId);
        return pWriter;
    }

    // OnPacketStatus: the grade, start tick, play time and claimed bits, as in Info.
    public static ByteWriter Status(int grade, int startTick, long playMs, int claimed) {
        var pWriter = Packet.Of(SendOp.PcBangBonus);
        pWriter.WriteByte((byte) Command.Status);
        WriteStatus(pWriter, grade, startTick, playMs, claimed);
        return pWriter;
    }

    public static ByteWriter Notice(NoticeCode code) {
        var pWriter = Packet.Of(SendOp.PcBangBonus);
        pWriter.WriteByte((byte) Command.Notice);
        pWriter.WriteByte((byte) code);
        return pWriter;
    }

    // 0x00B5: the field player's object id and PC bang grade.
    public static ByteWriter SetPcBang(int objectId, int grade) {
        var pWriter = Packet.Of(SendOp.SetPcBang);
        pWriter.WriteInt(objectId);
        pWriter.WriteByte((byte) grade);
        return pWriter;
    }

    private static void WriteStatus(ByteWriter pWriter, int grade, int startTick, long playMs, int claimed) {
        pWriter.WriteByte((byte) grade);
        pWriter.WriteInt(startTick);
        pWriter.WriteInt((int) Math.Clamp(playMs, 0, int.MaxValue));
        pWriter.WriteByte((byte) claimed);
    }

    // ItemEntity::Read: item id, rank (rarity), count and four flags.
    private static void WriteItemEntity(ByteWriter pWriter, int itemId, int rarity, int count) {
        pWriter.WriteInt(itemId);
        pWriter.WriteShort((short) rarity);
        pWriter.WriteInt(count);
        pWriter.WriteBool(false);
        pWriter.WriteBool(false);
        pWriter.WriteBool(false);
        pWriter.WriteBool(false);
    }
}

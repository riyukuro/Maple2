using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class WardrobeHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Wardrobe;

    private enum Command : byte {
        Create = 0,
        Equip = 1,
        Reset = 2,
        SetKey = 4,
        Rename = 6,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required BanWordStorage BanWordStorage { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Create:
                HandleCreate(session, packet);
                return;
            case Command.Equip:
                HandleEquip(session, packet);
                return;
            case Command.Reset:
                HandleReset(session, packet);
                return;
            case Command.SetKey:
                HandleSetKey(session, packet);
                return;
            case Command.Rename:
                HandleRename(session, packet);
                return;
        }
    }

    // creates wardrobe tab from current equips
    private void HandleCreate(GameSession session, IByteReader packet) {
        int index = packet.ReadInt();
        int type = packet.ReadInt();

        if (!session.Config.TryGetWardrobe(index, out Wardrobe? wardrobe)) {
            return;
        }

        // The type is the client's equip layer: 0 gear, 1 outfit, 2 the second outfit tab.
        if (type is < EquipKind.Gear or > EquipKind.Outfit2) {
            return;
        }

        wardrobe.Type = type;
        wardrobe.Equips.Clear();
        foreach ((EquipSlot slot, Item equip) in type switch {
            EquipKind.Outfit2 => session.Item.Equips.Outfit2,
            EquipKind.Outfit => session.Item.Equips.Outfit,
            _ => session.Item.Equips.Gear,
        }) {
            switch (slot) {
                // Weapons
                case EquipSlot.LH:
                case EquipSlot.RH:
                // Armor
                case EquipSlot.CP:
                case EquipSlot.MT:
                case EquipSlot.CL:
                case EquipSlot.PA:
                case EquipSlot.GL:
                case EquipSlot.SH:
                // Accessories
                case EquipSlot.FH:
                case EquipSlot.EY:
                case EquipSlot.EA:
                case EquipSlot.PD:
                case EquipSlot.RI:
                case EquipSlot.BE:
                    wardrobe.Equips[slot] = new Wardrobe.Equip(equip.Uid, equip.Id, slot, equip.Rarity);
                    break;
            }
        }

        session.Send(WardrobePacket.Load(index, wardrobe));
    }

    private void HandleEquip(GameSession session, IByteReader packet) {
        int index = packet.ReadInt();
        int layer = packet.ReadInt();
        if (layer is < EquipKind.Gear or > EquipKind.Outfit2) {
            return;
        }

        if (!session.Config.TryGetWardrobe(index, out Wardrobe? wardrobe)) {
            return;
        }

        // KMS2 ends every closet equip with a chat-and-alert notice, also when every item is already worn.
        // An item already worn is not equipped again (it is not in the bag, which would bring a "do not have"
        // box). The notice for an item that is gone is taken from the client's string table, not from KMS2.
        bool missing = false;
        lock (session.Item) {
            foreach (Wardrobe.Equip equip in wardrobe.Equips.Values) {
                if (session.Item.Equips.Get(equip.ItemUid) != null) {
                    continue;
                }
                if (session.Item.Inventory.Get(equip.ItemUid) == null) {
                    missing = true;
                    continue;
                }
                session.Item.Equips.Equip(equip.ItemUid, equip.EquipSlot, (byte) layer, closet: true);
            }
        }
        session.Send(NoticePacket.Notice(NoticePacket.Flags.Message | NoticePacket.Flags.Alert,
            missing ? StringCode.s_closet_msg_item_not_exist : StringCode.s_closet_msg_success));
    }

    private void HandleReset(GameSession session, IByteReader packet) {
        int index = packet.ReadInt();

        if (session.Config.TryGetWardrobe(index, out Wardrobe? wardrobe)) {
            wardrobe.Equips.Clear();
            wardrobe.Type = EquipKind.Gear; // KMS2 sends type 0 for a cleared tab, whatever it was made with.
            session.Send(WardrobePacket.Load(index, wardrobe));
        }
    }

    // This connects a wardrobe with a hotkey setting, which can be configured by KeyTable
    private void HandleSetKey(GameSession session, IByteReader packet) {
        int index = packet.ReadInt();
        int key = packet.ReadInt();

        if (session.Config.TryGetWardrobe(index, out Wardrobe? wardrobe)) {
            wardrobe.KeyId = key;
        }
    }

    private void HandleRename(GameSession session, IByteReader packet) {
        (int index, string name) = WardrobeRules.ReadRename(packet);
        bool allowed = WardrobeRules.NameAllowed(name, Constant.MaxClosetTabNameLength);
        bool banned = allowed && WardrobeRules.NameBanned(name, BanWordStorage.ContainsBannedWord);
        bool exists = session.Config.TryGetWardrobe(index, out Wardrobe? wardrobe);
        switch (WardrobeRules.Rename(allowed, banned, exists)) {
            case WardrobeRules.RenameStep.Refuse:
                session.Send(NoticePacket.MessageBox(StringCode.s_ban_check_err_any));
                return;
            case WardrobeRules.RenameStep.RefuseRestore:
                session.Send(NoticePacket.MessageBox(StringCode.s_ban_check_err_any));
                session.Send(WardrobePacket.Load(index, wardrobe!));
                return;
            case WardrobeRules.RenameStep.Rename:
                wardrobe!.Name = name;
                return;
            case WardrobeRules.RenameStep.Create:
                session.Config.AddWardrobe(name);
                return;
        }
    }
}

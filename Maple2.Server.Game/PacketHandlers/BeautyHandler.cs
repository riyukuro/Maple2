using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

public class BeautyHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Beauty;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required NpcMetadataStorage NpcMetadata { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private enum Command : byte {
        Shop = 0,
        CreateBeauty = 3,
        Unknown4 = 4, // UpdateEar?
        UpdateBeauty = 5,
        UpdateSkin = 6,
        RandomHair = 7,
        Warp = 10,
        ConfirmRandomHair = 12,
        SaveHair = 16,
        AddSlots = 17,
        DeleteHair = 18,
        AskAddSlots = 19,
        ApplySavedHair = 21,
        GearDye = 22,
        Voucher = 23,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Shop:
                HandleShop(session, packet);
                return;
            case Command.CreateBeauty:
                HandleCreateBeauty(session, packet);
                return;
            case Command.Unknown4:
                packet.ReadLong();
                return;
            case Command.UpdateBeauty:
                HandleUpdateBeauty(session, packet);
                return;
            case Command.UpdateSkin:
                HandleUpdateSkin(session, packet);
                return;
            case Command.RandomHair:
                HandleRandomHair(session, packet);
                return;
            case Command.Warp:
                HandleWarp(session, packet);
                return;
            case Command.ConfirmRandomHair:
                HandleConfirmRandomHair(session, packet);
                return;
            case Command.SaveHair:
                HandleSaveHair(session, packet);
                return;
            case Command.AddSlots:
                HandleAddSlots(session, packet);
                return;
            case Command.DeleteHair:
                HandleDeleteHair(session, packet);
                return;
            case Command.AskAddSlots:
                HandleAskAddSlots(session, packet);
                return;
            case Command.ApplySavedHair:
                HandleApplySavedHair(session, packet);
                return;
            case Command.GearDye:
                HandleGearDye(session, packet);
                return;
            case Command.Voucher:
                HandleVoucher(session, packet);
                return;
        }
    }

    private void HandleShop(GameSession session, IByteReader packet) {
        int npcId = packet.ReadInt();
        packet.ReadByte(); // the dialog type the client opened; the server takes it from the npc's own data (ShopType)

        if (!NpcMetadata.TryGet(npcId, out NpcMetadata? metadata)) {
            return;
        }

        if (!session.ServerTableMetadata.BeautyShopTable.Entries.TryGetValue(metadata.Basic.ShopId, out BeautyShopMetadata? shopMetadata)) {
            // TODO: Error?
            return;
        }

        // filter out non-usable genders
        List<BeautyShopItem> entries = FilterItemsByGender(session.Player.Value.Character.Gender, shopMetadata.Items).ToList();

        // Add date-based items
        if (shopMetadata.ItemGroups.Length > 0) {
            BeautyShopItemGroup? group = shopMetadata.ItemGroups
                .Where(group => group.StartTime < DateTime.Now.ToEpochSeconds())
                .OrderByDescending(group => group.StartTime)
                .FirstOrDefault();
            if (group != null) {
                entries.AddRange(FilterItemsByGender(session.Player.Value.Character.Gender, group.Items));
            }
        }

        session.BeautyShop = new BeautyShop(shopMetadata, entries.ToArray()) {
            Type = ShopType(session.ServerTableMetadata.ScriptFunctionTable, npcId, shopMetadata),
        };

        switch (session.BeautyShop.Type) {
            case BeautyShopType.Modify:
                switch (session.BeautyShop.Metadata.Category) {
                    case BeautyShopCategory.Mirror:
                        // No packet for mirror
                        return;
                    case BeautyShopCategory.Dye:
                        session.Send(BeautyPacket.DyeShop(session.BeautyShop));
                        break;
                    case BeautyShopCategory.Skin:
                        session.Send(BeautyPacket.BeautyShop(session.BeautyShop, session.Beauty.SlotCount, session.Beauty.SavedCount));
                        break;
                }
                break;
            case BeautyShopType.Save:
                session.Send(BeautyPacket.SaveShop(session.BeautyShop));
                session.Beauty.Load();
                break;
            case BeautyShopType.Default:
            case BeautyShopType.Random:
                session.Send(BeautyPacket.BeautyShop(session.BeautyShop, session.Beauty.SlotCount, session.Beauty.SavedCount));
                break;
            default:
                Logger.Error("Unknown beauty shop category: {Category}", session.BeautyShop.Metadata.Category);
                break;
        }
        return;

        IEnumerable<BeautyShopItem> FilterItemsByGender(Gender gender, BeautyShopItem[] items) {
            foreach (BeautyShopItem entry in items) {
                if (!ItemMetadata.TryGet(entry.Id, out ItemMetadata? itemMetadata)) {
                    continue;
                }

                if (itemMetadata.Limit.Gender == gender ||
                    itemMetadata.Limit.Gender == Gender.All) {
                    yield return entry;
                }
            }
        }
    }

    // The type of an npc's beauty shop: as the client derives it from the "kind,mode" arg of the npc's BeautyShopDialog
    // script function (GetDialogIdByKind, GetDialogType, uitalkcinematic.cpp:1981-2018). The byte the client sends
    // with the open is not trusted: it made shop 504 a type 4 shop (free recolors) or shop 508 a type 1 shop.
    // A BeautyStyleDialog function opens the style save dialog, type 4 (OpenBeautyStyleDialog, :1341). The names are
    // compared case-insensitively, as the client does (:1186).
    // Chosen here: an npc with no such function takes its shop row's type (random 2; skin, dye and mirror 3; else 1).
    public static BeautyShopType ShopType(ScriptFunctionTable functions, int npcId, BeautyShopMetadata shop) {
        ScriptFunctionMetadata? dialog = functions.Entries.TryGetValue(npcId, out Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>? states)
            ? states.Values.SelectMany(function => function.Values)
                .FirstOrDefault(function => string.Equals(function.UiName, "BeautyShopDialog", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(function.UiName, "BeautyStyleDialog", StringComparison.OrdinalIgnoreCase))
            : null;
        if (dialog != null && string.Equals(dialog.UiName, "BeautyStyleDialog", StringComparison.OrdinalIgnoreCase)) {
            return BeautyShopType.Save;
        }
        if (dialog?.UiArg != null) {
            string[] args = dialog.UiArg.Split(',');
            string kind = args[0];
            string mode = args.Length > 1 ? args[1] : string.Empty;
            BeautyShopType? type = DialogType(kind, mode);
            if (type != null) {
                return type.Value;
            }
        }
        return RowType(shop);

        static BeautyShopType? DialogType(string kind, string mode) {
            bool Is(string value, string name) => string.Equals(value, name, StringComparison.OrdinalIgnoreCase);
            if (Is(kind, "itemColor") || Is(kind, "mirror")) {
                return BeautyShopType.Modify;
            }
            if (!Is(kind, "hair") && !Is(kind, "makeUp") && !Is(kind, "face") && !Is(kind, "skin")) {
                return null;
            }
            if (mode.Length == 0) {
                return Is(kind, "skin") ? BeautyShopType.Modify : BeautyShopType.Default;
            }
            if (Is(mode, "random")) {
                return BeautyShopType.Random;
            }
            return Is(mode, "styleSave") ? BeautyShopType.Save : BeautyShopType.Default;
        }
    }

    // The type of a shop opened with no npc (a beauty coupon) or an npc with no dialog function: its row's (random 2;
    // skin, dye and mirror 3; else 1). The client takes the type the server sends (uibeautyshopdialog.cpp:1516).
    public static BeautyShopType RowType(BeautyShopMetadata shop) {
        if (shop.IsRandom) {
            return BeautyShopType.Random;
        }
        return shop.Category is BeautyShopCategory.Skin or BeautyShopCategory.Dye or BeautyShopCategory.Mirror
            ? BeautyShopType.Modify : BeautyShopType.Default;
    }

    // Whether an edit (sub-type 5) of this item is one the open shop's page shows: hair in a hair shop, a face in a face
    // shop, face decoration in a makeup shop (KR 504, 500 and 505 list items 102, 103 and 104). Another shop's
    // recolor fee is not this item's.
    private static bool Edits(BeautyShopCategory category, ItemType type) => category switch {
        BeautyShopCategory.Hair => type.IsHair,
        BeautyShopCategory.Face => type.IsFace,
        BeautyShopCategory.Makeup => type.IsDecal,
        _ => false,
    };

    private void HandleCreateBeauty(GameSession session, IByteReader packet) {
        (_, bool useVoucher, int itemId, _) = BeautyRules.ReadCreate(packet);

        if (session.BeautyShop == null) {
            return;
        }

        BeautyShopItem? entry = session.BeautyShop.Items.FirstOrDefault(entry => entry.Id == itemId);
        if (entry == null) {
            return;
        }
        if (BeautyRules.IsLocked(session.Player.Value.Character.Level, entry.RequiredLevel, entry.AchievementId, entry.AchievementRank,
                () => session.Achievement.HasAchievement(entry.AchievementId, entry.AchievementRank))) {
            return;
        }

        // The fee is taken only once the new item is on the character.
        Item? created = ReadBeauty(session, packet, entry.Id);
        if (created == null || !CanPay(session, useVoucher, session.BeautyShop, entry.Cost)) {
            return;
        }

        if (EquipBeauty(session, created)) {
            if (!Pay(session, useVoucher, session.BeautyShop, entry.Cost)) {
                Logger.Error("Beauty fee not taken after the new item {ItemId} of {CharacterId}", itemId, session.CharacterId);
            }
            session.ConditionUpdate(ConditionType.beauty_add, codeLong: itemId);
        }
    }

    private void HandleUpdateBeauty(GameSession session, IByteReader packet) {
        byte index = packet.ReadByte();
        bool useVoucher = packet.ReadBool();
        long uid = packet.ReadLong();

        if (session.BeautyShop == null) {
            return;
        }
        BeautyShop shop = session.BeautyShop;

        Item? cosmetic = session.Item.Equips.Get(uid);
        if (cosmetic == null) {
            return;
        }

        // Only for cap rotation/positioning. Used in mirror npc and also when changing hair while hat is equipped.
        if (cosmetic.Type.IsHat) {
            var appearance = packet.ReadClass<CapAppearance>();
            cosmetic.Appearance = appearance;
            session.Field?.Broadcast(ItemUpdatePacket.Update(session.Player, cosmetic));
            return;
        }

        // A random shop's client sends sub-type 7, never 5 (uibeautyshopdialog.cpp:1841-1856), and its fee would be the
        // style fee (GetFee, :1313); refuse it rather than make the edit free. A style save shop's dialog (type 4)
        // never sends 5 either: refused, not free.
        if (shop.Type is BeautyShopType.Random or BeautyShopType.Save) {
            return;
        }
        // Chosen here: only a hair, face or makeup shop takes an edit, of its own kind of item; a skin, dye or mirror
        // shop's dialog has no hair, face or makeup page to send it from, and its recolor fee is not for these items.
        if (!Edits(shop.Metadata.Category, cosmetic.Type)) {
            return;
        }
        // The fee is taken only once the changed item is on the character.
        Item? changed = ReadBeauty(session, packet, cosmetic.Id);
        if (changed == null) {
            return;
        }
        // The client's fee of an edited item (UIBeautyShopDialog::GetFee, uibeautyshopdialog.cpp:1282-1320): in a
        // shop of type 1 or 3 its recolor fee when its color changed, else none (a crop shows no price, :1960).
        EquipColor? startColor = cosmetic.Appearance?.Color;
        bool recolor = startColor != null && !Equals(changed.Appearance?.Color, startColor);
        BeautyShopCostMetadata? fee = recolor ? shop.Metadata.ColorCostMetadata : null;
        if (fee != null && !CanPay(session, useVoucher, shop, fee)) {
            return;
        }

        if (!EquipBeauty(session, changed)) {
            return;
        }
        if (fee != null && !Pay(session, useVoucher, shop, fee)) {
            Logger.Error("Beauty fee not taken after the change of item {ItemId} of {CharacterId}", cosmetic.Id, session.CharacterId);
        }
        if (fee != null) {
            session.ConditionUpdate(ConditionType.beauty_change_color, codeLong: cosmetic.Id);
        }
    }

    private void HandleUpdateSkin(GameSession session, IByteReader packet) {
        byte index = packet.ReadByte();
        var skinColor = packet.Read<SkinColor>();
        bool useVoucher = packet.ReadBool();

        // The skin page is the skin shop's (kind skin, KR 501); another shop's recolor fee is not a skin color's.
        if (session.BeautyShop?.Metadata.Category != BeautyShopCategory.Skin) {
            return;
        }

        if (useVoucher) {
            if (!PayWithVoucher(session, session.BeautyShop)) {
                return;
            }
        } else {
            // A skin color is a recolor: the shop's recolor fee (KR skin shop 501: 150 merets, no style fee).
            if (!PayWithCurrency(session, session.BeautyShop, session.BeautyShop.Metadata.ColorCostMetadata)) {
                return;
            }
        }

        session.Player.Value.Character.SkinColor = skinColor;
        session.Field?.Broadcast(UserSkinColorPacket.Update(session.Player, skinColor));
    }

    private void HandleRandomHair(GameSession session, IByteReader packet) {
        int shopId = packet.ReadInt();
        bool useVoucher = packet.ReadBool();
        packet.ReadLong(); // the long the shop-open packet carried after its two flag bytes (UIBeautyShopDialog m_2a0), sent back unchanged

        // The client sends 7 only from a random shop's dialog (type 2, uibeautyshopdialog.cpp:1841): a style shop's
        // style fee (0 in the KR hair shop 504) paid for a random pick of its rows.
        if (session.BeautyShop == null || session.BeautyShop.Id != shopId || session.BeautyShop.Type != BeautyShopType.Random) {
            return;
        }
        BeautyShop shop = session.BeautyShop;

        if (useVoucher) {
            if (!PayWithVoucher(session, shop)) {
                return;
            }
        } else {
            if (!PayWithCurrency(session, shop, shop.Metadata.StyleCostMetadata)) {
                return;
            }
        }

        WeightedSet<BeautyShopItem> weightedSet = new();
        foreach (BeautyShopItem item in shop.Items) {
            weightedSet.Add(item, item.Weight);
        }

        BeautyShopItem entry = weightedSet.Get();

        if (!ItemMetadata.TryGet(entry.Id, out ItemMetadata? itemMetadata)) {
            return;
        }
        DefaultHairMetadata defaultHairMetadata = itemMetadata.DefaultHairs[Random.Shared.Next(itemMetadata.DefaultHairs.Length)];

        // Grab random hair from default hair metadata
        double frontLength = Random.Shared.NextDouble() * (defaultHairMetadata.MaxScale - defaultHairMetadata.MinScale + defaultHairMetadata.MinScale);
        double backLength = Random.Shared.NextDouble() * (defaultHairMetadata.MaxScale - defaultHairMetadata.MinScale + defaultHairMetadata.MinScale);

        // Get random color
        if (!TableMetadata.ColorPaletteTable.Entries.TryGetValue(Constant.HairPaletteId, out IReadOnlyDictionary<int, ColorPaletteTable.Entry>? palette)) {
            return;
        }

        int colorIndex = Random.Shared.Next(palette.Count);
        ColorPaletteTable.Entry? colorEntry = palette.Values.ElementAtOrDefault(colorIndex);
        if (colorEntry == null) {
            return;
        }

        var hairAppearance = new HairAppearance(new EquipColor(colorEntry.Primary, colorEntry.Secondary, colorEntry.Tertiary, Constant.HairPaletteId, colorIndex),
            (float) backLength, defaultHairMetadata.BackPosition, defaultHairMetadata.BackRotation, (float) frontLength, defaultHairMetadata.FrontPosition,
            defaultHairMetadata.FrontRotation);

        Item? newHair = session.Field?.ItemDrop.CreateItem(entry.Id);
        if (newHair == null) {
            return;
        }
        newHair.Appearance = hairAppearance;
        newHair.Group = ItemGroup.Outfit;

        using GameStorage.Request db = session.GameStorage.Context();
        newHair = db.CreateItem(session.CharacterId, newHair);
        if (newHair == null) {
            return;
        }

        // Save old hair
        Item? prevHair = session.Item.Equips.Get(EquipSlot.HR);
        if (prevHair == null) {
            Logger.Error("Player {PlayerId} has no previous hair to save for random hair", session.Player.Value.Character.Id);
            return;
        }
        session.Beauty.SavePreviousHair(prevHair);

        if (!session.Item.Equips.EquipCosmetic(newHair, EquipSlot.HR)) {
            Logger.Error("Player {PlayerId} failed to equip random hair {HairId}", session.Player.Value.Character.Id, newHair.Id);
        }
        session.Send(BeautyPacket.RandomHair(prevHair.Id, newHair.Id));
        session.ConditionUpdate(ConditionType.beauty_random, codeLong: newHair.Id);
    }
    private void HandleWarp(GameSession session, IByteReader packet) {
        short type = packet.ReadShort();
        int mapId = type switch {
            1 => Constants.BeautyHairShopGotoFieldID,
            3 => Constants.BeautyFaceShopGotoFieldID,
            5 => Constants.BeautyColorShopGotoFieldID,
            _ => 0,
        };
        int portalId = type switch {
            1 => Constants.BeautyHairShopGotoPortalID,
            3 => Constants.BeautyFaceShopGotoPortalID,
            5 => Constants.BeautyColorShopGotoPortalID,
            _ => 0,
        };

        session.Send(session.PrepareField(mapId, portalId: portalId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }

    private void HandleConfirmRandomHair(GameSession session, IByteReader packet) {
        bool newHairSelected = packet.ReadBool();
        int voucherItemId = 0;
        // The answer to a random pick (the client's keep or return box after sub-type 7): with no pick waiting it gave
        // the shop's return coupon each time it was sent.
        if (session.BeautyShop == null || !session.Beauty.HasPreviousHair) {
            return;
        }
        if (!newHairSelected) {
            session.Beauty.SelectPreviousHair();
            Item? voucher = session.Field?.ItemDrop.CreateItem(session.BeautyShop.Metadata.ReturnCouponId);
            if (voucher != null && !session.Item.Inventory.Add(voucher, true)) {
                session.Item.MailItem(voucher);
                voucherItemId = voucher.Id;
            }
        }

        session.Send(BeautyPacket.RandomHairResult(voucherItemId));
        session.Beauty.ClearPreviousHair();
    }

    private static void HandleSaveHair(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();
        if (!InStyleSave(session)) {
            return;
        }

        session.Beauty.AddHair(uid);
    }

    // The client sends this instead of SaveHair when its slots are full, after the player agreed to the
    // extension's price (uibeautyshopstyledialog.cpp:986, 1196; uibeautyshopdialog.cpp:1754).
    private static void HandleAddSlots(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();
        if (!InStyleSave(session) || !session.Beauty.CanAddHair(uid)) {
            return;
        }

        if (BeautyRules.SaveNeedsExtension(session.Beauty.OpenSlots) && !session.Beauty.ExtendSlots()) {
            return;
        }
        session.Beauty.AddHair(uid);
    }

    private void HandleDeleteHair(GameSession session, IByteReader packet) {
        (long uid, _) = BeautyRules.ReadDelete(packet);

        if (!InStyleSave(session)) {
            return;
        }

        session.Beauty.RemoveHair(uid);
    }

    // The extend button of the saved style list, after the price box (uibeautyshopstyledialog.cpp:1007, 1136).
    private static void HandleAskAddSlots(GameSession session, IByteReader packet) {
        if (!InStyleSave(session)) {
            return;
        }
        session.Beauty.ExtendSlots();
    }

    private void HandleApplySavedHair(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();
        byte index = packet.ReadByte();

        if (!InStyleSave(session)) {
            return;
        }

        // Nothing is charged for a uid that is not a saved style.
        if (!session.Beauty.HasSavedCosmetic(uid)) {
            return;
        }

        // The fee is taken only once the saved style is on the character.
        BeautyShopCostMetadata cost = session.BeautyShop.Metadata.StyleCostMetadata;
        if (CanPay(session, false, session.BeautyShop, cost) && session.Beauty.EquipSavedCosmetic(uid)) {
            if (!Pay(session, false, session.BeautyShop, cost)) {
                Logger.Error("Beauty fee not taken after the saved style {Uid} of {CharacterId}", uid, session.CharacterId);
            }
        }
    }

    // The saved style list (sub-types 16-19, 21) is the style save dialog's (0x4f03, type 4,
    // uibeautyshopstyledialog.cpp:444-1742), opened only for a style save npc; its saved styles are loaded only there.
    private static bool InStyleSave(GameSession session) => session.BeautyShop?.Type == BeautyShopType.Save;

    private void HandleGearDye(GameSession session, IByteReader packet) {
        byte count = packet.ReadByte();
        // The dye window is the item color shop's (kind itemColor, KR 506); another shop's recolor fee is not a dye's.
        if (session.BeautyShop?.Metadata.Category != BeautyShopCategory.Dye) {
            return;
        }
        for (byte i = 0; i < count; i++) {
            (bool UseVoucher, long Uid, int ItemId)? head = BeautyRules.ReadDyeHead(packet);
            if (head == null) {
                continue;
            }
            (bool useVoucher, long itemUid, int itemId) = head.Value;

            Item? item = session.Item.Equips.Get(itemUid);
            if (item == null || !BeautyRules.DyeItemMatches(item.Id, itemId)) {
                // Only this entry is refused. The client counts one answer per entry and takes an error
                // as the answer of one (uiitemcoloringdialog.cpp:939-955, 1044). Its colors are read by the
                // slot of the item it names, so the next entry starts at its own first byte.
                session.Send(BeautyPacket.Error(BeautyError.s_beauty_msg_error_color));
                bool cap;
                if (item != null) {
                    cap = item.EquipSlot() == EquipSlot.CP;
                } else if (ItemMetadata.TryGet(itemId, out ItemMetadata? named)) {
                    cap = named.SlotNames.Contains(EquipSlot.CP);
                } else {
                    return; // the length of the entry is not known
                }
                BeautyRules.ReadDyeColors(packet, cap);
                continue;
            }

            // The colors are read before the fee is taken: a short packet charges nothing.
            ItemAppearance appearance = BeautyRules.ReadDyeColors(packet, item.EquipSlot() == EquipSlot.CP);
            if (!Pay(session, useVoucher, session.BeautyShop, session.BeautyShop.Metadata.ColorCostMetadata)) {
                // The client waits for one answer per entry: an item update or an error with a code
                // other than 0 (uiitemcoloringdialog.cpp:577-591, 939-955, 1044, 1321-1327), and while
                // it waits it refuses to restart the window (:1248). A fee paid in currency has sent
                // its error; a missing voucher has sent a notice only, which the client does not count.
                if (useVoucher) {
                    session.Send(BeautyPacket.Error(BeautyError.lack_currency));
                }
                continue;
            }

            item.Appearance = appearance;
            session.Field?.Broadcast(ItemUpdatePacket.Update(session.Player, item));
            // A paid dye counts for beauty_change_color (conditionutil.cpp:615), as the KR trophy
            // 23100191 (items 11050001-15999999) needs.
            session.ConditionUpdate(ConditionType.beauty_change_color, codeLong: item.Id);
        }
    }

    private void HandleVoucher(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();

        Item? voucher = session.Item.Inventory.Get(uid);
        if (voucher == null || voucher.Metadata.Function?.Type != ItemFunction.ItemChangeBeauty) {
            return;
        }

        if (!int.TryParse(voucher.Metadata.Function.Parameters, out int shopId)) {
            return;
        }

        if (!session.ServerTableMetadata.BeautyShopTable.Entries.TryGetValue(shopId, out BeautyShopMetadata? shopMetadata) ||
            shopMetadata.Items.Length == 0) {
            return;
        }

        // Shop all must be the same type (hair, face, etc). Will not work correctly if mixed. Assumes all items are the same type, so we'll just check the first one.
        if (!ItemMetadata.TryGet(shopMetadata.Items.First().Id, out ItemMetadata? itemEntry)) {
            return;
        }

        var shop = new BeautyShop(shopMetadata, shopMetadata.Items) {
            Type = RowType(shopMetadata),
        };

        session.BeautyShop = shop;
        session.Send(BeautyPacket.BeautyShop(shop, session.Beauty.SlotCount, session.Beauty.SavedCount));
    }

    // Whether the fee can be taken; nothing is taken. Packets of one session are handled one at a time, so
    // a Pay after the change finds what this found.
    private static bool CanPay(GameSession session, bool useVoucher, BeautyShop shop, BeautyShopCostMetadata cost) {
        cost = SaleCost(session, shop, cost);
        if (useVoucher) {
            ItemTag voucherTag = shop.Metadata.CouponTag;
            if (voucherTag == ItemTag.None) {
                return false;
            }
            if (!BeautyRules.Affords(session.Item.Inventory.Find(voucherTag).Sum(item => (long) item.Amount), 1)) {
                session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert, StringCode.s_err_invalid_item));
                return false;
            }
            return true;
        }

        MeretCost.Wallet? wallet = MeretCost.OfShop(cost.CurrencyType);
        long owned = cost.CurrencyType switch {
            ShopCurrencyType.Meso => session.Currency.Meso,
            _ when wallet is { } meretWallet => MeretCost.Owned(meretWallet, session.Currency.Meret, session.Currency.GameMeret, cost.Price),
            ShopCurrencyType.Item => session.Item.Inventory.Find(cost.PaymentItemId).Sum(item => (long) item.Amount),
            _ => ToCurrencyType(cost.CurrencyType) is var type && type != CurrencyType.None ? session.Currency[type] : -1,
        };
        if (!BeautyRules.Affords(owned, cost.Price)) {
            SendLack(session, wallet);
            return false;
        }
        return true;
    }

    // The client's lack text for the fee (ShowLackCurrencyMessage, uicommon.cpp:1083-1105): currencies 3 and 10 ask
    // to buy merets, 9 (the red merets only) is a notice of its own; Beauty has no error code for it.
    private static void SendLack(GameSession session, MeretCost.Wallet? wallet) {
        switch (wallet) {
            case null:
                session.Send(BeautyPacket.Error(BeautyError.lack_currency));
                break;
            case MeretCost.Wallet.Red:
                session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_err_lack_merat_red));
                break;
            default:
                session.Send(BeautyPacket.Error(BeautyError.s_err_lack_merat_ask));
                break;
        }
    }

    private static bool Pay(GameSession session, bool useVoucher, BeautyShop shop, BeautyShopCostMetadata cost) {
        return useVoucher ? PayWithVoucher(session, shop) : PayWithCurrency(session, shop, cost);
    }

    // The fee a running SaleBeautyShop event of the shop leaves, the fee the shop's window shows
    // (EventSaleRules.BeautyShopPrice).
    private static BeautyShopCostMetadata SaleCost(GameSession session, BeautyShop shop, BeautyShopCostMetadata cost) {
        int rate = EventSaleRules.BeautyShopRate(session.FindEvent(GameEventType.SaleBeautyShop), shop.Id);
        int price = EventSaleRules.BeautyShopPrice(cost.Price, rate);
        return price == cost.Price ? cost : cost with { Price = price };
    }

    private static CurrencyType ToCurrencyType(ShopCurrencyType type) {
        return type switch {
            ShopCurrencyType.ValorToken => CurrencyType.ValorToken,
            ShopCurrencyType.Treva => CurrencyType.Treva,
            ShopCurrencyType.Rue => CurrencyType.Rue,
            ShopCurrencyType.HaviFruit => CurrencyType.HaviFruit,
            ShopCurrencyType.StarPoint => CurrencyType.StarPoint,
            ShopCurrencyType.MenteeToken => CurrencyType.MenteeToken,
            ShopCurrencyType.MentorToken => CurrencyType.MentorToken,
            ShopCurrencyType.MesoToken => CurrencyType.MesoToken,
            ShopCurrencyType.ReverseCoin => CurrencyType.ReverseCoin,
            _ => CurrencyType.None,
        };
    }

    private static bool PayWithVoucher(GameSession session, BeautyShop shop) {
        ItemTag voucherTag = shop.Metadata.CouponTag;

        if (voucherTag == ItemTag.None) {
            return false;
        }

        var ingredient = new IngredientInfo(voucherTag, 1);
        if (!session.Item.Inventory.Consume([ingredient])) {
            session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert, StringCode.s_err_invalid_item));
            return false;
        }

        session.Send(BeautyPacket.Voucher(shop.Metadata.CouponId, 1));
        return true;
    }

    private static bool PayWithCurrency(GameSession session, BeautyShop shop, BeautyShopCostMetadata cost) {
        cost = SaleCost(session, shop, cost);
        switch (cost.CurrencyType) {
            case ShopCurrencyType.Meso:
                if (session.Currency.CanAddMeso(-cost.Price) != -cost.Price) {
                    session.Send(BeautyPacket.Error(BeautyError.lack_currency));
                    return false;
                }
                session.Currency.ChangeMeso(-cost.Price);
                break;
            case ShopCurrencyType.Meret:
            case ShopCurrencyType.EventMeret:
            case ShopCurrencyType.GameMeret:
                if (!session.Currency.TryTakeMerets(MeretCost.OfShop(cost.CurrencyType)!.Value, cost.Price, out _)) {
                    SendLack(session, MeretCost.OfShop(cost.CurrencyType));
                    return false;
                }
                break;
            case ShopCurrencyType.Item:
                var ingredient = new ItemComponent(cost.PaymentItemId, -1, cost.Price, ItemTag.None);
                if (!session.Item.Inventory.ConsumeItemComponents([ingredient])) {
                    session.Send(BeautyPacket.Error(BeautyError.lack_currency));
                    return false;
                }
                break;
            default:
                CurrencyType currencyType = ToCurrencyType(cost.CurrencyType);
                if (currencyType == CurrencyType.None || session.Currency[currencyType] < cost.Price) {
                    session.Send(BeautyPacket.Error(BeautyError.lack_currency));
                    return false;
                }

                session.Currency.Change(currencyType, -cost.Price);
                break;
        }
        return true;
    }

    // The new item with the appearance the packet carries; nothing is stored yet.
    private static Item? ReadBeauty(GameSession session, IByteReader packet, int itemId) {
        Item? newCosmetic = session.Field?.ItemDrop.CreateItem(itemId, 1, 1);
        if (newCosmetic == null) {
            return null;
        }

        if (newCosmetic.Type.IsHair) {
            newCosmetic.Appearance = packet.ReadClass<HairAppearance>();
        } else if (newCosmetic.Type.IsDecal) {
            newCosmetic.Appearance = packet.ReadClass<DecalAppearance>();
        } else if (newCosmetic.Type.IsHat) {
            newCosmetic.Appearance = packet.ReadClass<CapAppearance>();
        } else {
            newCosmetic.Appearance = packet.ReadClass<ItemAppearance>();
        }
        return newCosmetic;
    }

    private static bool EquipBeauty(GameSession session, Item newCosmetic) {
        using GameStorage.Request db = session.GameStorage.Context();
        Item? stored = db.CreateItem(session.CharacterId, newCosmetic);
        if (stored == null) {
            return false;
        }
        newCosmetic = stored;

        if (!session.Item.Equips.EquipCosmetic(newCosmetic, newCosmetic.Metadata.SlotNames.First())) {
            db.SaveItems(0, newCosmetic);
            return false;
        }

        return true;
    }
}

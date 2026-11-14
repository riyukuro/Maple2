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
using Maple2.Server.Game.Manager.Items;
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
        // The client builds it (an item taken off in a preview, Customize::Equipment::SendRemoved) but never sends it:
        // its one caller, Customize::CommitUid, is called nowhere.
        RemoveBeauty = 4,
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
            case Command.RemoveBeauty:
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

    // The client sends the open only from the talk whose OpenDialog named a beauty dialog, with that talk's npc
    // (UITalkManager::OpenTalkDialog, uitalkcinematic.cpp:1357-1399); a shop of another npc, or one opened with no
    // such talk, is not opened. The offer is taken: a second open needs a new talk.
    private void HandleShop(GameSession session, IByteReader packet) {
        int npcId = packet.ReadInt();
        packet.ReadByte(); // the dialog type the client opened; the server takes it from the npc's own data (ShopType)

        int offered = session.BeautyNpcOffered;
        session.BeautyNpcOffered = 0;
        if (npcId == 0 || npcId != offered) {
            Logger.Debug("Beauty shop of npc {NpcId} not offered (talk offered {Offered})", npcId, offered);
            return;
        }

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

        // A pick waiting for its answer belongs to the shop it was made in; the client's keep or return box is modal, so
        // a shop opened after it ends it, and the answer cannot take another shop's return coupon.
        session.Beauty.ClearPreviousHair();
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
    }

    // The rows of a shop the character's gender can wear.
    private IEnumerable<BeautyShopItem> FilterItemsByGender(Gender gender, BeautyShopItem[] items) {
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

    // The type of an npc's beauty shop: as the client derives it from the "kind,mode" arg of the npc's BeautyShopDialog
    // script function (GetDialogIdByKind, GetDialogType, uitalkcinematic.cpp:1981-2018). The byte the client sends
    // with the open is not trusted: it made shop 504 a type 4 shop (free recolors) or shop 508 a type 1 shop.
    // A BeautyStyleDialog function opens the style save dialog, type 4 (OpenBeautyStyleDialog, :1341). The names are
    // compared case-insensitively, as the client does (:1186).
    // Chosen here: an npc with no such function takes its shop row's type (random 2; skin, dye and mirror 3; else 1).
    // The npc talk dialogs that open a beauty shop (GetDialogIdByKind, uitalkcinematic.cpp:1186, case-insensitive).
    public static bool IsShopDialog(string? uiName) =>
        string.Equals(uiName, "BeautyShopDialog", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uiName, "BeautyStyleDialog", StringComparison.OrdinalIgnoreCase);

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

        // A random shop's dialog sends sub-type 7 for its rows, never 3 (uibeautyshopdialog.cpp:1840-1856), and the style
        // save dialog (type 4) sends only 16-21: a 3 there chose a row of the random pool at the row's own fee.
        if (session.BeautyShop == null || session.BeautyShop.Type is BeautyShopType.Random or BeautyShopType.Save) {
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

        // The fee is stored with the new item (WearPaid).
        Item? created = ReadBeauty(session, packet, entry.Id);
        if (created == null || !CanWear(session, created)) {
            return;
        }
        // The item's own default color is never the dialog's pick (it resets to none, uibeautyshopdialog.cpp:702).
        EquipColor? createdColor = created.Appearance?.Color;
        if (createdColor is { } picked && !(picked.PaletteId == created.Metadata.Customize.ColorPalette
                                            && picked.Index == created.Metadata.Customize.DefaultColorIndex)
            && RefusedColor(session, picked)) {
            return;
        }
        bool discounted = !useVoucher && SaleCost(session, session.BeautyShop, entry.Cost).Price < entry.Cost.Price;
        if (HoldFee(session, useVoucher, session.BeautyShop, entry.Cost) is not { } fee) {
            return;
        }
        if (WearPaid(session, created, fee) is { } stored) {
            if (discounted && stored.Appearance is HairAppearance) {
                session.Beauty.MarkDiscounted(stored.Uid);
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
        // The fee is stored with the changed item (WearPaid).
        Item? changed = ReadBeauty(session, packet, cosmetic.Id);
        if (changed == null || !CanWear(session, changed)) {
            return;
        }
        // The client's fee of an edited item (UIBeautyShopDialog::GetFee, uibeautyshopdialog.cpp:1282-1320): in a
        // shop of type 1 or 3 its recolor fee when its color changed, else none (a crop shows no price, :1960).
        EquipColor? startColor = cosmetic.Appearance?.Color;
        bool recolor = startColor != null && !Equals(changed.Appearance?.Color, startColor);
        if (recolor && changed.Appearance?.Color is { } newColor && RefusedColor(session, newColor)) {
            return;
        }
        HeldFee? fee = null;
        if (recolor && (fee = HoldFee(session, useVoucher, shop, shop.Metadata.ColorCostMetadata)) == null) {
            return;
        }

        if (WearPaid(session, changed, fee) != null && recolor) {
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

        // A skin color is a recolor: the shop's recolor fee (KR skin shop 501: 150 merets, no style fee). The fee and
        // the color are stored together: the fee was taken first and the color kept in memory for the next save.
        if (HoldFee(session, useVoucher, session.BeautyShop, session.BeautyShop.Metadata.ColorCostMetadata) is not { } fee) {
            return;
        }
        var held = new List<int>();
        if (!session.TrySavePurchase(db => SaveFee(session, db, fee, held) && db.SaveSkinColor(session.Player.Value, skinColor))) {
            ReleaseFee(session, fee);
            Logger.Error("Skin color of {CharacterId} not stored; nothing taken", session.CharacterId);
            return;
        }
        SettleFee(session, fee, held);
        SpentFee(session, fee);

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
        double frontLength = RandomScale(defaultHairMetadata.MinScale, defaultHairMetadata.MaxScale, Random.Shared.NextDouble());
        double backLength = RandomScale(defaultHairMetadata.MinScale, defaultHairMetadata.MaxScale, Random.Shared.NextDouble());

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

        Item? prevHair = session.Item.Equips.Get(EquipSlot.HR);
        if (prevHair == null) {
            Logger.Error("Player {PlayerId} has no previous hair to save for random hair", session.Player.Value.Character.Id);
            return;
        }

        // The fee is held once every lookup above passed (it was taken first, so a pick that found no hair row, no
        // palette or no worn hair took the fee and gave nothing), and stored with the new hair (WearPaid): the hair
        // used to be stored on its own after the fee, which only the next session save stored.
        if (!CanWear(session, newHair)) {
            return;
        }
        if (HoldFee(session, useVoucher, shop, shop.Metadata.StyleCostMetadata) is not { } fee) {
            return;
        }
        Item? worn = WearPaid(session, newHair, fee);
        if (worn == null) {
            return;
        }
        newHair = worn;
        session.Beauty.SavePreviousHair(prevHair);
        session.Send(BeautyPacket.RandomHair(prevHair.Id, newHair.Id));
        session.ConditionUpdate(ConditionType.beauty_random, codeLong: newHair.Id);
    }

    // A random hair length between the default hair's smallest and largest scale (roll in [0, 1)). It was
    // roll * max, which went below the smallest scale.
    public static double RandomScale(float min, float max, double roll) => min + roll * (max - min);

    // The game menu's and the beauty list's move to a salon (UIGameMenuDialog::SendBeauty, uigamemenudialog.cpp:2267;
    // UIBeautyListDialog, uibeautylistdialog.cpp:111-139): 1 the hair salon, 3 the face shop, 5 the dye shop, sent
    // after a confirm box only. The client checks nothing else before it; its texts for a refused move
    // (s_beauty_goto_map_invalid_dead, _battle, _samefield) are the server's to send, and it never shows them itself.
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

        if (mapId == 0 || session.Field == null) {
            return;
        }
        StringCode? refusal = WarpRefusal(session.Player.IsDead, session.Player.InBattle, session.Field.MapId == mapId);
        if (refusal != null) {
            session.Send(NoticePacket.Message(refusal.Value, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
            return;
        }

        session.SendFieldEnter(session.PrepareField(mapId, portalId: portalId));
    }

    // A dead player (a tombstone), one in combat and one already in the salon's field do not move.
    public static StringCode? WarpRefusal(bool dead, bool inBattle, bool sameField) {
        if (dead) {
            return StringCode.s_beauty_goto_map_invalid_dead;
        }
        if (inBattle) {
            return StringCode.s_beauty_goto_map_invalid_battle;
        }
        return sameField ? StringCode.s_beauty_goto_map_invalid_samefield : null;
    }

    private void HandleConfirmRandomHair(GameSession session, IByteReader packet) {
        // The style the player keeps in the client's keep or return box (UIBeautyShopDialog::SendBeautyMode12): 0 the hair
        // worn before the pick, 1 the new hair. No other value is sent.
        byte slot = packet.ReadByte();
        if (slot > 1) {
            return;
        }
        bool newHairSelected = slot == 1;
        int voucherItemId = 0;
        // The answer to a random pick (the client's keep or return box after sub-type 7): with no pick waiting it gave
        // the shop's return coupon each time it was sent.
        if (session.BeautyShop == null || !session.Beauty.HasPreviousHair) {
            return;
        }
        bool mailed = false;
        if (!newHairSelected) {
            session.Beauty.SelectPreviousHair();
            Item? voucher = session.Field?.ItemDrop.CreateItem(session.BeautyShop.Metadata.ReturnCouponId);
            if (voucher != null) {
                // The client's answer (op 12, uibeautyshopdialog.cpp:1557) names the coupon given back, and whether it
                // went by mail; 0 says no coupon. The coupon added to the bag used to be answered as none.
                if (session.Item.Inventory.Add(voucher, true)) {
                    voucherItemId = voucher.Id;
                } else if (session.Item.MailItem(voucher)) {
                    voucherItemId = voucher.Id;
                    mailed = true;
                }
            }
        }

        session.Send(BeautyPacket.RandomHairResult(voucherItemId, mailed));
        session.Beauty.ClearPreviousHair();
    }

    private static void HandleSaveHair(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();
        if (!InStyleSave(session) || RefusedDiscountedSave(session, uid)) {
            return;
        }

        session.Beauty.AddHair(uid);
    }

    // A hair bought at a sale price while a SaleBeautyShop event still runs: the style dialog answers error 25 with "discounted
    // hair cannot be stored during the event" (uibeautyshopstyledialog.cpp:1347) and has no check of its own. Before
    // any extension fee.
    private static bool RefusedDiscountedSave(GameSession session, long uid) {
        if (!session.Beauty.IsDiscounted(uid)
            || !session.FindEvent(GameEventType.SaleBeautyShop).Any(gameEvent => gameEvent.Metadata.Data is SaleBeautyShop { Flag: false })) {
            return false;
        }
        session.Send(BeautyPacket.Error(BeautyError.s_beauty_msg_error_style_save_cant_not_beauty_day));
        return true;
    }

    // The client sends this instead of SaveHair when its slots are full, after the player agreed to the
    // extension's price (uibeautyshopstyledialog.cpp:986, 1196; uibeautyshopdialog.cpp:1754).
    private static void HandleAddSlots(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();
        if (!InStyleSave(session) || !session.Beauty.CanAddHair(uid) || RefusedDiscountedSave(session, uid)) {
            return;
        }

        bool extend = BeautyRules.SaveNeedsExtension(session.Beauty.OpenSlots);
        if (extend && !session.Beauty.ExtendSlots()) {
            return;
        }
        // A save that extended the slots is answered with 17 (both dialogs read 16 and 17 alike; the shop dialog also
        // adds the extension to its slot count on 17, uibeautyshopdialog.cpp:1577).
        session.Beauty.AddHair(uid, extended: extend);
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

        // Nothing is charged for a uid that is not a saved style. The fee is stored with the style's copy (WearPaid).
        Item? copy = session.Beauty.SavedCopy(uid);
        if (copy == null) {
            return;
        }
        // The dialog's apply checks the style's gender first (OnClickSave, uibeautyshopstyledialog.cpp:926) and has an
        // answer for both refusals it cannot check itself (:1331-1365): a style of the other gender (23) and the style
        // already worn (24). Neither takes the fee.
        if (copy.Metadata.Limit.Gender != Gender.All && copy.Metadata.Limit.Gender != session.Player.Value.Character.Gender) {
            session.Send(BeautyPacket.Error(BeautyError.s_beauty_msg_error_style_disable_gender));
            return;
        }
        if (session.Item.Equips.Get(EquipSlot.HR) is { } wornHair && SameLook(wornHair, copy)) {
            session.Send(BeautyPacket.Error(BeautyError.s_beauty_msg_error_style_apply_already));
            return;
        }
        if (!CanWear(session, copy)) {
            return;
        }
        if (HoldFee(session, false, session.BeautyShop, session.BeautyShop.Metadata.StyleCostMetadata) is not { } fee) {
            return;
        }
        Item? worn = WearPaid(session, copy, fee);
        if (worn == null) {
            return;
        }
        session.Send(BeautyPacket.ApplySavedHair());
        session.ConditionUpdate(ConditionType.beauty_style_apply, codeLong: worn.Id);
    }

    // The same hair with the same appearance (color, lengths and positions as the client sends them).
    private static bool SameLook(Item worn, Item style) {
        if (worn.Id != style.Id || worn.Appearance == null || style.Appearance == null) {
            return false;
        }
        var wornBytes = new ByteWriter();
        wornBytes.WriteClass<ItemAppearance>(worn.Appearance);
        var styleBytes = new ByteWriter();
        styleBytes.WriteClass<ItemAppearance>(style.Appearance);
        return wornBytes.ToArray().AsSpan().SequenceEqual(styleBytes.ToArray());
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
            if (!Equals(appearance.Color, item.Appearance?.Color) && RefusedColor(session, appearance.Color)) {
                continue;
            }
            if (HoldFee(session, useVoucher, session.BeautyShop, session.BeautyShop.Metadata.ColorCostMetadata) is not { } fee) {
                // The client waits for one answer per entry: an item update or an error with a code
                // other than 0 (uiitemcoloringdialog.cpp:577-591, 939-955, 1044, 1321-1327), and while
                // it waits it refuses to restart the window (:1248). A fee paid in currency has sent
                // its error; a missing voucher has sent a notice only, which the client does not count.
                if (useVoucher) {
                    session.Send(BeautyPacket.Error(BeautyError.lack_currency));
                }
                continue;
            }

            // The fee and the dyed item's row are stored together: the color was kept in memory for the next save,
            // the fee taken on its own. The item keeps its color when the store is not kept.
            ItemAppearance? before = item.Appearance;
            var held = new List<int>();
            if (!session.TrySavePurchase(db => {
                item.Appearance = appearance;
                return SaveFee(session, db, fee, held) && db.SaveItems(session.CharacterId, item);
            }, () => item.Appearance = before)) {
                ReleaseFee(session, fee);
                Logger.Error("Dye of item {Uid} of {CharacterId} not stored; nothing taken", item.Uid, session.CharacterId);
                session.Send(BeautyPacket.Error(BeautyError.s_beauty_msg_error_color));
                continue;
            }
            SettleFee(session, fee, held);
            SpentFee(session, fee);
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

        // The voucher shop lists only what the character can wear, as an npc's shop does: a row of the other gender
        // was offered and its create refused only by the equip check, after the dialog showed it.
        BeautyShopItem[] rows = FilterItemsByGender(session.Player.Value.Character.Gender, shopMetadata.Items).ToArray();
        if (rows.Length == 0) {
            return;
        }
        var shop = new BeautyShop(shopMetadata, rows) {
            Type = RowType(shopMetadata),
        };

        session.Beauty.ClearPreviousHair();
        session.BeautyShop = shop;
        session.Send(BeautyPacket.BeautyShop(shop, session.Beauty.SlotCount, session.Beauty.SavedCount));
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

    // A beauty fee taken in memory and not yet stored (HoldFee): the change's transaction stores it (SaveFee), then
    // SettleFee sends it, or ReleaseFee gives it back when the transaction was not kept; RefundFee gives back a stored
    // one. The fee used to be taken on its own, after the change was stored or applied (or before it, for a skin color,
    // a dye and a random pick), and stored only by the next session save: a failed store of either half kept the
    // other. Meso, merets: the take's packet waits for SettleFee. Currency: TryTake sends it at once. Payment items
    // and vouchers: nothing is taken until the transaction writes their rows (Stacks).
    private sealed record HeldFee(ShopCurrencyType Type, int Price, long MesoSends, bool Red, CurrencyType Currency,
                                  bool Voucher, int CouponId, List<(long Uid, int Amount, int ItemId, int Rarity)>? Stacks);

    // Takes the fee in memory (see HeldFee), or answers the lack and returns null. A voucher pays with one item of
    // the shop's coupon tag.
    private static HeldFee? HoldFee(GameSession session, bool useVoucher, BeautyShop shop, BeautyShopCostMetadata cost) {
        if (useVoucher) {
            ItemTag voucherTag = shop.Metadata.CouponTag;
            if (voucherTag == ItemTag.None) {
                return null;
            }
            IReadOnlyList<(long Uid, int Amount)>? plan = session.Item.Inventory.PlanConsume([new IngredientInfo(voucherTag, 1)]);
            List<(long Uid, int Amount, int ItemId, int Rarity)>? vouchers = plan == null ? null : Stacks(session, plan);
            if (vouchers == null) {
                session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert, StringCode.s_err_invalid_item));
                return null;
            }
            return new HeldFee(ShopCurrencyType.Item, 1, 0, false, CurrencyType.None, true, shop.Metadata.CouponId, vouchers);
        }

        cost = SaleCost(session, shop, cost);
        switch (cost.CurrencyType) {
            case ShopCurrencyType.Meso:
                // Checked and taken in one step; a negative price is refused.
                long sends = session.Currency.MesoSends;
                if (!session.Currency.TryTakeMeso(cost.Price, notify: false)) {
                    session.Send(BeautyPacket.Error(BeautyError.lack_currency));
                    return null;
                }
                return new HeldFee(cost.CurrencyType, cost.Price, sends, false, CurrencyType.None, false, 0, null);
            case ShopCurrencyType.Meret:
            case ShopCurrencyType.EventMeret:
            case ShopCurrencyType.GameMeret:
                MeretCost.Wallet wallet = MeretCost.OfShop(cost.CurrencyType)!.Value;
                if (!session.Currency.TryTakeMerets(wallet, cost.Price, out bool red, notify: false)) {
                    SendLack(session, wallet);
                    return null;
                }
                return new HeldFee(cost.CurrencyType, cost.Price, 0, red, CurrencyType.None, false, 0, null);
            case ShopCurrencyType.Item:
                List<(long Uid, int Amount, int Rarity)>? planned = cost.Price < 0 ? null
                    : session.Item.Inventory.PlanConsume(cost.PaymentItemId, -1, cost.Price);
                if (planned == null) {
                    session.Send(BeautyPacket.Error(BeautyError.lack_currency));
                    return null;
                }
                return new HeldFee(cost.CurrencyType, cost.Price, 0, false, CurrencyType.None, false, 0,
                    planned.Select(stack => (stack.Uid, stack.Amount, cost.PaymentItemId, stack.Rarity)).ToList());
            default:
                CurrencyType currencyType = ToCurrencyType(cost.CurrencyType);
                if (currencyType == CurrencyType.None || cost.Price < 0 || !session.Currency.TryTake(currencyType, cost.Price)) {
                    session.Send(BeautyPacket.Error(BeautyError.lack_currency));
                    return null;
                }
                return new HeldFee(cost.CurrencyType, cost.Price, 0, false, currencyType, false, 0, null);
        }

        static List<(long Uid, int Amount, int ItemId, int Rarity)>? Stacks(GameSession session, IReadOnlyList<(long Uid, int Amount)> plan) {
            var stacks = new List<(long Uid, int Amount, int ItemId, int Rarity)>();
            foreach ((long uid, int amount) in plan) {
                Item? item = session.Item.Inventory.Get(uid);
                if (item == null) {
                    return null;
                }
                stacks.Add((uid, amount, item.Id, item.Rarity));
            }
            return stacks;
        }
    }

    // Writes the paying stacks' rows as the take leaves them in db's transaction, and takes them from the bag once it
    // is committed (the wallet is stored by the purchase save itself); held gets the ids taken, for SettleFee.
    private bool SaveFee(GameSession session, GameStorage.Request db, HeldFee fee, List<int> held) {
        if (fee.Stacks == null) {
            return true;
        }
        foreach ((long uid, int amount, _, _) in fee.Stacks) {
            if (!session.Item.Inventory.SaveConsumed(db, uid, amount)) {
                return false;
            }
        }
        db.AfterCommit(() => {
            foreach ((long uid, int amount, _, _) in fee.Stacks) {
                if (!session.Item.Inventory.ConsumeStored(uid, amount, held)) {
                    Logger.Error("Beauty fee item {Uid} x{Amount} stored as taken but not in the bag of {CharacterId}", uid, amount, session.CharacterId);
                }
            }
        });
        return true;
    }

    // Sends a fee once its change is stored, after the save (a held count's quest stores itself). A meret fee's
    // use_merat waits for SpentFee: a fee given back is not counted as spent.
    private static void SettleFee(GameSession session, HeldFee fee, List<int> held) {
        session.Item.Inventory.UpdateHeld(held);
        if (fee.Voucher) {
            session.Send(BeautyPacket.Voucher(fee.CouponId, 1));
            return;
        }
        switch (fee.Type) {
            case ShopCurrencyType.Meso:
                session.Currency.NotifyMeso();
                break;
            case ShopCurrencyType.Meret:
            case ShopCurrencyType.EventMeret:
            case ShopCurrencyType.GameMeret:
                if (fee.Red) {
                    session.Currency.NotifyGameMeretTaken(fee.Price, report: false);
                } else {
                    session.Currency.NotifyMeretTaken(fee.Price, report: false);
                }
                break;
        }
    }

    // Counts a settled fee that was kept as merets spent (use_merat).
    private static void SpentFee(GameSession session, HeldFee? fee) {
        if (fee is { Voucher: false, Type: ShopCurrencyType.Meret or ShopCurrencyType.EventMeret or ShopCurrencyType.GameMeret }) {
            session.Currency.ReportMeretSpent(-fee.Price);
        }
    }

    // Gives back a held fee whose change was not stored (the paying items were not taken).
    private static void ReleaseFee(GameSession session, HeldFee fee) {
        if (fee.Voucher) {
            return;
        }
        switch (fee.Type) {
            case ShopCurrencyType.Meso:
                session.Currency.ReturnHeldMeso(fee.Price, fee.MesoSends);
                break;
            case ShopCurrencyType.Meret:
            case ShopCurrencyType.EventMeret:
            case ShopCurrencyType.GameMeret:
                if (fee.Red) {
                    session.Currency.ReturnGameMeret(fee.Price);
                } else {
                    session.Currency.ReturnMeret(fee.Price);
                }
                break;
            case ShopCurrencyType.Item:
                break;
            default:
                session.Currency.Return(fee.Currency, fee.Price);
                break;
        }
    }

    // Gives back a stored and sent fee whose change could not be applied after its store. Paying items come back as
    // new copies of the ids and rarities taken, to the bag or else by mail; one that cannot be given is logged.
    private void RefundFee(GameSession session, HeldFee fee) {
        switch (fee.Type) {
            case ShopCurrencyType.Meso:
                session.Currency.ReturnMeso(fee.Price);
                break;
            case ShopCurrencyType.Item:
                foreach ((_, int amount, int itemId, int rarity) in fee.Stacks ?? []) {
                    Item? copy = session.Item.CreateItem(itemId, rarity, amount);
                    if (copy == null || !session.Item.AddOrMail(copy, true, commit: true)) {
                        Logger.Error("Beauty fee item {ItemId} x{Amount} (rarity {Rarity}) could not be given back to {CharacterId}",
                            itemId, amount, rarity, session.CharacterId);
                    }
                }
                break;
            case ShopCurrencyType.Meret:
            case ShopCurrencyType.EventMeret:
            case ShopCurrencyType.GameMeret:
                if (fee.Red) {
                    session.Currency.ReturnGameMeret(fee.Price);
                } else {
                    session.Currency.ReturnMeret(fee.Price);
                }
                break;
            default:
                session.Currency.Return(fee.Currency, fee.Price);
                break;
        }
        session.SaveRefund();
    }

    // Whether cosmetic can go on: its one slot is hair, face or face decoration, and the client's equip checks pass
    // (refused with their message box). Checked before the fee is held, so a refused item takes nothing.
    // A palette color whose trophy grade the character lacks, refused with the color error before any fee. The client
    // checks the color it picked from a palette the same way on save (UIBeautyShopDialog, uibeautyshopdialog.cpp:1419;
    // the dye window, uiitemcoloringdialog.cpp:1004): IsPaletteColorLocked (uibeautyshopstyledialog.cpp:748) leaves
    // palette 0, a row without a trophy or grade and a trophy it has no data for unlocked, else locks it until the
    // grade is reached (achievemanager.cpp:447). Never stricter than the client: the same exceptions hold here.
    private static bool RefusedColor(GameSession session, EquipColor color) {
        if (!PaletteLocked(session, color)) {
            return false;
        }
        session.Send(BeautyPacket.Error(BeautyError.s_beauty_msg_error_color));
        return true;
    }

    internal static bool PaletteLocked(GameSession session, EquipColor color) {
        if (color.PaletteId <= 0 || color.Index < 0
            || !session.TableMetadata.ColorPaletteTable.Entries.TryGetValue(color.PaletteId, out IReadOnlyDictionary<int, ColorPaletteTable.Entry>? palette)
            || !palette.TryGetValue(color.Index, out ColorPaletteTable.Entry? row)) {
            return false;
        }
        if (row.AchieveId <= 0 || row.AchieveGrade <= 0 || !session.AchievementMetadata.TryGet(row.AchieveId, out _)) {
            return false;
        }
        return !session.Achievement.HasAchievement(row.AchieveId, row.AchieveGrade);
    }

    private static bool CanWear(GameSession session, Item cosmetic) {
        if (cosmetic.Metadata.SlotNames.Length == 0 || cosmetic.Metadata.SlotNames[0] is not (EquipSlot.HR or EquipSlot.FA or EquipSlot.FD)) {
            return false;
        }
        StringCode result = EquipManager.ValidateEquipItem(session, cosmetic);
        if (result != StringCode.s_empty_string) {
            session.Send(NoticePacket.MessageBox(result));
            return false;
        }
        return true;
    }

    // Stores cosmetic (a new item, no uid yet) with its fee in one transaction, then puts it on: the stored item, or
    // null when nothing was kept. A failed store gives the held fee back; an item that could not go on after its store
    // is given up and its fee given back.
    private Item? WearPaid(GameSession session, Item cosmetic, HeldFee? fee) {
        var held = new List<int>();
        Item? stored = null;
        if (!session.TrySavePurchase(db => (fee == null || SaveFee(session, db, fee, held))
                                           && (stored = db.CreateItem(session.CharacterId, cosmetic)) != null) || stored == null) {
            if (fee != null) {
                ReleaseFee(session, fee);
            }
            Logger.Error("Beauty item {ItemId} of {CharacterId} not stored; nothing taken", cosmetic.Id, session.CharacterId);
            return null;
        }
        if (fee != null) {
            SettleFee(session, fee, held);
        }

        if (!session.Item.Equips.EquipCosmetic(stored, stored.Metadata.SlotNames[0])) {
            Logger.Error("Beauty item {Uid} of {CharacterId} stored but not worn; fee given back", stored.Uid, session.CharacterId);
            using (GameStorage.Request db = session.GameStorage.Context()) {
                db.SaveItems(0, stored);
            }
            if (fee != null) {
                RefundFee(session, fee);
            }
            return null;
        }
        SpentFee(session, fee);
        return stored;
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
}

using System.Collections.Concurrent;
using System.Numerics;
using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;
using static Maple2.Model.Error.CharacterCreateError;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemUseHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestItemUse;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required GameStorage GameStorage { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required BanWordStorage BanWordStorage { private get; init; }
    // ReSharper restore All
    #endregion

    // The checks an item use passes before its function runs: not expired (pets and maid furnishings excepted), a
    // premium-club item only with premium club, a shadow world item only in the shadow world. RequestItemBox runs them
    // too (ItemBoxHandler).
    public static bool UseAllowed(GameSession session, Item item) {
        if (!ItemLockRules.ExpiryAllowsUse(item.IsExpired(), item.Metadata.Property.Type, MaidRules.MaidId(item.Metadata, false))) {
            return false;
        }
        if (item.Metadata.Limit.RequireVip && session.Player.Value.Account.PremiumTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
            return false;
        }
        return item.Metadata.Function?.OnlyShadowWorld != true || session.Field?.Metadata.Property.Continent == Continent.ShadowWorld;
    }

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        long itemUid = packet.ReadLong();

        Item? item = session.Item.Inventory.Get(itemUid);
        if (item == null) {
            Logger.Warning("RequestItemUse for invalid item:{ItemUid}", itemUid);
            return;
        }

        if (!UseAllowed(session, item)) {
            return;
        }

        switch (item.Metadata.Function?.Type) {
            case ItemFunction.BlueprintImport:
                HandleBlueprintImport(session, item);
                break;
            case ItemFunction.StoryBook:
                HandleStoryBook(session, item);
                break;
            case ItemFunction.ChatEmoticonAdd:
                HandleChatSticker(session, item);
                break;
            case ItemFunction.EnchantScroll:
                HandleEnchantScroll(session, item);
                break;
            case ItemFunction.ItemRemakeScroll:
                HandleItemRemakeScroll(session, item);
                break;
            case ItemFunction.ItemSocketScroll:
                HandleItemSocketScroll(session, item);
                break;
            case ItemFunction.TitleScroll:
                HandleTitleScroll(session, item);
                break;
            case ItemFunction.OpenCoupleEffectBox:
                HandleBuddyBadgeBox(session, packet, item);
                break;
            case ItemFunction.ExpendCharacterSlot:
                HandleExpandCharacterSlot(session, item);
                break;
            case ItemFunction.ChangeCharName:
                HandleChangeCharacterName(session, packet, item);
                break;
            case ItemFunction.ChangeGender:
                HandleChangeGender(session, item);
                break;
            case ItemFunction.VIPCoupon:
                HandlePremiumClubCoupon(session, item);
                break;
            case ItemFunction.SelectItemBox:
                HandleSelectItemBox(session, packet, item);
                break;
            case ItemFunction.OpenItemBox:
            case ItemFunction.OpenItemBoxWithKey:
                HandleOpenItemBox(session, item);
                break;
            case ItemFunction.OpenGachaBox:
                HandleOpenGacha(session, packet, item);
                break;
            case ItemFunction.OpenItemBoxLullu:
                HandleOpenItemBoxLullu(session, packet, item);
                break;
            case ItemFunction.OpenItemBoxLulluSimple:
                HandleOpenItemBoxLulluSimple(session, packet, item);
                break;
            case ItemFunction.ItemRePackingScroll:
                HandleItemRepackingScroll(session, item);
                break;
            case ItemFunction.ItemChangeBeauty:
                HandleItemChangeBeauty(session, item);
                break;
            case ItemFunction.InstallBillBoard:
                HandleInstallBillBoard(session, packet, item);
                break;
            case ItemFunction.OpenMassive:
                HandleOpenMassive(session, packet, item);
                break;
            case ItemFunction.DefenseGuard:
                HandleDefenseGuard(session, item);
                break;
            case ItemFunction.LevelPotion:
                HandleLevelPotion(session, item);
                break;
            case ItemFunction.SurvivalLevelExp:
                HandleSurvivalLevelExp(session, item);
                break;
            case ItemFunction.CreateReactor:
                HandleCreateReactor(session, item);
                break;
            case ItemFunction.FestivalEvent:
                HandleFestivalEvent(session, item);
                break;
            case ItemFunction.WeddingExpItem:
                HandleWeddingExpItem(session, item);
                break;
            case ItemFunction.HongBao:
                HandleHongBao(session, item);
                break;
            case ItemFunction.AddAdditionalEffect:
                HandleAddAdditionalEffect(session, item);
                break;
            case ItemFunction.ExpandInven:
                HandleExpandInventory(session, item);
                break;
            case ItemFunction.QuestScroll:
                HandleQuestScroll(session, item);
                break;
            case ItemFunction.RecallParty:
                HandleRecallParty(session, item);
                break;
            case ItemFunction.RecallGuild:
                HandleRecallGuild(session, item);
                break;
            case ItemFunction.RecallWedding:
                HandleRecallWedding(session, item);
                break;
            case ItemFunction.CallAirTaxi:
                HandleCallAirTaxi(session, packet, item);
                break;
            case ItemFunction.ItemActiveCodiTab:
                HandleItemActiveCodiTab(session, item);
                break;
            case ItemFunction.OpenItemBoxLimitedQuantity:
                HandleOpenItemBoxLimitedQuantity(session, item);
                break;
            case ItemFunction.OpenPetBox:
                HandleOpenPetBox(session, packet, item);
                break;
            case ItemFunction.QuestStart:
                HandleQuestStart(session, item);
                break;
            case ItemFunction.TreasureMap:
                HandleTreasureMap(session, packet, item);
                break;
            case ItemFunction.OpenBank:
                HandleOpenBank(session);
                break;
            case ItemFunction.OpenInstrument:
                // The client plays the instrument itself: its OpenInstrument use (0x140d23c50) enters the play
                // state, which opens the play dialog, and only then sends this use, with no reply read. The
                // server takes the instrument from PlayInstrument (InstrumentHandler), which checks the item.
                break;
            case ItemFunction.CallBlackMarket:
                HandleCallBlackMarket(session);
                break;
            case ItemFunction.CallMedic:
                HandleCallMedic(session, item);
                break;
            case ItemFunction.OpenDungeonItemBox:
                HandleOpenDungeonItemBox(session, item);
                break;
            case ItemFunction.PetTraining:
                HandlePetTraining(session, item, packet);
                break;
            case ItemFunction.UGCMapPackage:
                HandleUgcMapPackage(session, item, packet);
                break;
            case ItemFunction.ResetAbilityCoolTime:
                HandleResetAbilityCoolTime(session, item);
                break;
            case ItemFunction.ChangeGuildName:
                HandleChangeGuildName(session, item, packet);
                break;
            case ItemFunction.PetExtraction:
                HandlePetExtraction(session, item, packet);
                break;
            case ItemFunction.CashAttendEvent:
                HandleCashAttendEvent(session, item);
                break;
            default:
                Logger.Warning("Unhandled item function: {Name}", item.Metadata.Function?.Type);
                return;
        }
    }

    private void HandleBlueprintImport(GameSession session, Item item) {
        if (item.Blueprint is null) {
            Logger.Error("Item {ItemUid} is missing blueprint", item.Uid);
            return;
        }

        Plot? plot = session.Housing.GetIndoorPlot();
        if (plot is null) {
            session.Send(NoticePacket.Message(StringCode.s_ugcmap_not_use_blueprint_item, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
            return;
        }

        if (plot.Cubes.Count != 0) {
            session.Send(NoticePacket.Message(StringCode.s_err_ugcmap_package_clear_indoor_first, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        HomeLayout? layout = db.GetHomeLayout(item.Blueprint.BlueprintUid);
        if (layout == null) {
            return;
        }

        if (!session.Housing.RequestLayout(layout, out (Dictionary<FurnishingCurrencyType, long> cubeCosts, int cubeCount) result)) {
            return;
        }

        session.Housing.StagedItemBlueprint = item.Blueprint;
        session.Housing.StagedBlueprintItemUid = item.Uid;
        // The client has a blueprint bill pending for this item (FUN_140ee8610, uiugccubebilldialog.cpp:181) and
        // confirms it with RequestCube 70. The bill is not a preview, so the confirm is sent.
        session.Send(CubePacket.CalculateEstimate(result.cubeCosts, result.cubeCount, preview: false));
    }

    // An interior package (UgcMapPackageRules): the text is "false" or "true". The client's checks are made again, with
    // its messages: the owner's indoor home, a package the server has, and for "false" a home of the package's size
    // with no cubes. Storage room is checked, then the package is used up and built (HousingManager.InstallPackage).
    private static void HandleUgcMapPackage(GameSession session, Item item, IByteReader packet) {
        if (UgcMapPackageRules.Confirmed(packet.ReadUnicodeString()) is not { } confirmed) {
            return;
        }
        void Refuse(StringCode code) => session.Send(NoticePacket.Message(code, NoticePacket.Flags.Message | NoticePacket.Flags.Alert));

        if (session.Field is not HomeFieldManager home || session.Field.MapId != session.Player.Value.Home.Indoor.MapId) {
            Refuse(StringCode.s_err_ugcmap_package_should_use_in_indoor);
            return;
        }
        if (home.OwnerId != session.AccountId) {
            Refuse(StringCode.s_err_ugcmap_package_cant_use_in_others_home);
            return;
        }
        Plot? plot = session.Housing.GetIndoorPlot();
        if (plot is null || plot.IsPlanner) {
            Refuse(StringCode.s_err_ugcmap_package_not_a_valid_indoor);
            return;
        }
        string name = item.Metadata.Function?.Parameters.Trim() ?? "";
        if (name.Length == 0 || !session.MapMetadata.TryGetExportedUgc(name, out ExportedUgcMapMetadata? package)) {
            Refuse(StringCode.s_err_ugcmap_package_not_a_valid_package_item);
            return;
        }
        Home owner = session.Player.Value.Home;
        if (UgcMapPackageRules.Refusal(confirmed, owner.Area, owner.Height, plot.Cubes.Count, UgcMapPackageRules.Size(package)) is { } refusal) {
            Refuse(refusal);
            return;
        }
        if (session.Housing.InstallPackage(plot, package, () => session.Item.Inventory.Consume(item.Uid, 1), out _) is { } failed) {
            Refuse(failed);
        }
    }

    private static void HandleStoryBook(GameSession session, Item item) {
        if (!int.TryParse(item.Metadata.Function?.Parameters, out int storyBookId)) {
            return;
        }

        session.ConditionUpdate(ConditionType.openStoryBook, codeLong: storyBookId);
        session.Send(StoryBookPacket.Load(storyBookId));
    }

    private static void HandleChatSticker(GameSession session, Item item) {
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);

        if (!parameters.ContainsKey("id") || !int.TryParse(parameters["id"], out int stickerSetId)) {
            session.Send(ChatStickerPacket.Error(ChatStickerError.s_msg_chat_emoticon_add_failed));
            return;
        }

        int duration = 0;
        if (parameters.TryGetValue("durationSec", out string? durationString)) {
            int.TryParse(durationString, out duration);
        }

        long? held = session.Player.Value.Unlock.StickerSets.TryGetValue(stickerSetId, out long set) ? set : null;
        long? newTime = ChatStickerRules.NewExpiry(held, duration, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (newTime == null) {
            session.Send(ChatStickerPacket.Error(ChatStickerError.s_msg_chat_emoticon_add_failed_already_exist));
            return;
        }

        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }

        session.Player.Value.Unlock.StickerSets[stickerSetId] = newTime.Value;

        session.Send(ChatStickerPacket.Add(item, new ChatSticker(stickerSetId, session.Player.Value.Unlock.StickerSets[stickerSetId])));
    }

    private void HandleEnchantScroll(GameSession session, Item item) {
        if (!int.TryParse(item.Metadata.Function?.Parameters, out int enchantId)) {
            session.Send(EnchantScrollPacket.Error(EnchantScrollError.s_enchantscroll_invalid_scroll));
            return;
        }

        if (!TableMetadata.EnchantScrollTable.Entries.TryGetValue(enchantId, out EnchantScrollMetadata? metadata)) {
            session.Send(EnchantScrollPacket.Error(EnchantScrollError.s_enchantscroll_invalid_scroll));
            return;
        }

        session.Send(EnchantScrollPacket.UseScroll(item, metadata));
    }

    private static void HandleItemRemakeScroll(GameSession session, Item item) {
        session.Send(ChangeAttributesScrollPacket.UseScroll(item));
    }

    private void HandleItemSocketScroll(GameSession session, Item item) {
        if (!int.TryParse(item.Metadata.Function?.Parameters, out int scrollId)) {
            session.Send(ItemSocketScrollPacket.Error(ItemSocketScrollError.s_itemsocket_scroll_error_server_default));
            return;
        }

        if (!TableMetadata.ItemSocketScrollTable.Entries.TryGetValue(scrollId, out ItemSocketScrollMetadata? metadata)) {
            session.Send(ItemSocketScrollPacket.Error(ItemSocketScrollError.s_itemsocket_scroll_error_server_default));
            return;
        }

        session.Send(ItemSocketScrollPacket.UseScroll(item, metadata));
    }

    private static void HandleTitleScroll(GameSession session, Item item) {
        if (!int.TryParse(item.Metadata.Function?.Parameters, out int titleId)) {
            return;
        }

        if (session.Player.Value.Unlock.HasTitle(titleId)) {
            session.Send(ChatPacket.Alert(StringCode.s_title_scroll_duplicate_err));
            return;
        }

        if (session.Item.Inventory.Consume(item.Uid, 1)) {
            session.Send(UserEnvPacket.AddTitle(titleId));
            session.Player.Value.Unlock.AddTitle(titleId);
        }
    }

    private void HandleBuddyBadgeBox(GameSession session, IByteReader packet, Item item) {
        string targetUser = packet.ReadUnicodeString();

        if (targetUser == session.PlayerName) {
            session.Send(NoticePacket.MessageBox(StringCode.s_couple_effect_error_openbox_myself_char));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long characterId = db.GetCharacterId(targetUser);
        if (!session.PlayerInfo.GetOrFetch(characterId, out PlayerInfo? receiverInfo)) {
            session.Send(NoticePacket.MessageBox((StringCode.s_couple_effect_error_openbox_charname)));
            return;
        }

        if (receiverInfo.AccountId == session.Player.Value.Character.AccountId) {
            session.Send(NoticePacket.MessageBox(StringCode.s_couple_effect_error_openbox_myself_account));
            return;
        }

        int[] buddyBadgeBoxParams = item.Metadata.Function?.Parameters.Split(',').Select(int.Parse).ToArray() ?? [];
        if (buddyBadgeBoxParams.Length != 2) {
            session.Send(NoticePacket.MessageBox(StringCode.s_couple_effect_error_openbox_unknown));
            Logger.Error("Invalid buddy badge box parameters: {Parameters}", item.Metadata.Function?.Parameters);
            return;
        }

        Item? selfBadge = session.Field?.ItemDrop.CreateItem(buddyBadgeBoxParams[0], buddyBadgeBoxParams[1]);
        if (selfBadge == null) {
            session.Send(NoticePacket.MessageBox(StringCode.s_couple_effect_error_openbox_unknown));
            Logger.Error("Failed to create buddy badge box item: {Parameters}", buddyBadgeBoxParams[0]);
            return;
        }
        selfBadge.CoupleInfo = new ItemCoupleInfo(receiverInfo.CharacterId, receiverInfo.Name, true);

        if (!session.Item.Inventory.CanAdd(selfBadge)) {
            session.Send(NoticePacket.MessageBox(StringCode.s_gem_error_inventory_full));
            return;
        }

        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            Logger.Error("Failed to use buddy badge box: {ItemUid}", item.Uid);
            return;
        }

        var receiverMail = new Mail() {
            ReceiverId = receiverInfo.CharacterId,
            Type = MailType.System,
            ContentArgs = [
                ("str", $"{session.PlayerName}"),
            ],
        };

        receiverMail.SetTitle(StringCode.s_couple_effect_mail_title_receiver);
        receiverMail.SetContent(StringCode.s_couple_effect_mail_content_receiver);
        receiverMail.SetSenderName(StringCode.s_couple_effect_mail_sender);

        receiverMail = db.CreateMail(receiverMail);
        if (receiverMail == null) {
            session.Send(NoticePacket.MessageBox((StringCode.s_couple_effect_error_openbox_unknown)));
            throw new InvalidOperationException($"Failed to create buddy badge mail for receiver character id: {receiverInfo.CharacterId}");
        }

        Item? receiverItem = session.Field?.ItemDrop.CreateItem(buddyBadgeBoxParams[0], buddyBadgeBoxParams[1]);
        if (receiverItem == null) {
            throw new InvalidOperationException($"Failed to create buddy badge item: {buddyBadgeBoxParams[0]}");
        }
        receiverItem.CoupleInfo = new ItemCoupleInfo(session.Player.Value.Character.Id, session.PlayerName);
        receiverItem = db.CreateItem(receiverMail.Id, receiverItem);
        if (receiverItem == null) {
            throw new InvalidOperationException($"Failed to create buddy badge: {buddyBadgeBoxParams[0]}");
        }

        receiverMail.Items.Add(receiverItem);

        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = receiverInfo.CharacterId,
                MailId = receiverMail.Id,
            });
        } catch { /* ignored */
        }

        session.Item.Inventory.Add(selfBadge, true);
        session.Send(NoticePacket.MessageBox(new InterfaceText(StringCode.s_couple_effect_mail_send_partner, receiverInfo.Name)));
    }

    private static void HandleExpandCharacterSlot(GameSession session, Item item) {
        if (session.Player.Value.Account.MaxCharacters >= Constant.ServerMaxCharacters) {
            session.Send(ItemUsePacket.MaxCharacterSlots());
            return;
        }

        if (session.Item.Inventory.Consume(item.Uid, 1)) {
            session.Player.Value.Account.MaxCharacters++;
            session.Send(ItemUsePacket.CharacterSlotAdded());
        }
    }

    // The gender voucher. KMS2: the voucher leaves the bag, hair, face, face decoration and ear
    // come off (ItemPutOff), a Notice 5 message box closes the game scene, and the character list
    // then shows the other gender with new items in those slots. The client shows the notice's text
    // as it is (CSceneStateGame::OnNotice, kind 5), so it has to be the done text,
    // s_change_gender_result_success (756 in this client). The KMS2 notice carried id 755 on build
    // 2521; in this client's table 755 is the worn-items refusal, which showed after a change that
    // went through.
    private void HandleChangeGender(GameSession session, Item item) {
        Character character = session.Player.Value.Character;
        Gender gender = GenderChangeRules.Other(character.Gender);

        var picks = new List<(EquipSlot Slot, ItemMetadata Metadata)>();
        foreach (EquipSlot slot in GenderChangeRules.LookSlots) {
            if (!TableMetadata.DefaultItemsTable.Common.TryGetValue(slot, out int[]? ids)) {
                continue;
            }
            var defaults = new List<(int Id, Gender Limit)>();
            foreach (int defaultId in ids) {
                if (session.ItemMetadata.TryGet(defaultId, out ItemMetadata? metadata)) {
                    defaults.Add((defaultId, metadata.Limit.Gender));
                }
            }
            if (session.ItemMetadata.TryGet(GenderChangeRules.PickDefault(defaults, gender), out ItemMetadata? pick)) {
                picks.Add((slot, pick));
            }
        }

        // One lock over the checks and the change.
        lock (session.Item) {
            // The client refuses both by itself: the wedding state with its own text, the worn items with this one.
            if (!GenderChangeRules.WeddingAllows(session.Marriage.Marriage.Status)) {
                session.Send(NoticePacket.MessageBox(StringCode.s_change_gender_result_failed));
                return;
            }
            foreach (ConcurrentDictionary<EquipSlot, Item> tab in new[] { session.Item.Equips.Gear, session.Item.Equips.Outfit, session.Item.Equips.Outfit2 }) {
                foreach ((EquipSlot slot, Item worn) in tab) {
                    if (GenderChangeRules.BlocksChange(slot, worn.Metadata.Limit.Gender)) {
                        session.Send(NoticePacket.MessageBox(StringCode.s_change_gender_err_equip_items));
                        return;
                    }
                }
            }

            bool changed;
            using (GameStorage.Request db = session.GameStorage.Context()) {
                changed = GenderChangeRules.Apply(character, session.Item.Equips.Outfit, picks,
                    store: look => db.CreateItem(session.CharacterId, look),
                    unstore: look => db.SaveItems(0, look),
                    consume: () => session.Item.Inventory.Consume(item.Uid, 1),
                    takeOff: old => {
                        // As an unequip of a look item: discarded, never back to the bag.
                        old.Group = ItemGroup.Default;
                        old.Slot = -1;
                        session.Item.Inventory.Discard(old);
                        session.Field?.Broadcast(EquipPacket.UnequipItem(session.Player, old));
                        session.Buffs.RemoveItemBuffs(old);
                    });
            }
            if (!changed) {
                session.Send(NoticePacket.MessageBox(StringCode.s_change_gender_result_failed));
                return;
            }
        }

        // Stored at once, so a crash before the logout save cannot give back the voucher and the old look
        // next to the new look's rows.
        session.SavePurchase(db => {
            db.SavePlayer(session.Player);
            session.Item.Equips.Save(db);
            session.Item.Inventory.Save(db);
        });

        session.Send(NoticePacket.Disconnect(StringCode.s_change_gender_result_success));
    }

    private void HandleChangeCharacterName(GameSession session, IByteReader packet, Item item) {
        string newName = packet.ReadUnicodeString();

        // The same checks as the name check before the confirm box. The game scene reads no
        // CharacterCreate packet, so a refusal is a message box with the client's name change text.
        int width = NameValidator.CharacterNameWidth(newName, Target.LOCALE);
        bool blank = CharacterCreateRules.IsBlankName(newName);
        bool banned = !blank && BanWordStorage.ContainsBannedWord(newName);
        using GameStorage.Request db = GameStorage.Context();
        bool taken = !blank && db.GetCharacterId(newName) != default;
        NameChangeRules.Refusal refusal = NameChangeRules.Check(blank, width, Constant.CharacterNameLengthMin, Constant.CharacterNameLengthMax, banned, taken);
        if (refusal != NameChangeRules.Refusal.None) {
            (StringCode code, string[] args) = NameChangeRules.Text(refusal, newName);
            session.Send(NoticePacket.MessageBox(new InterfaceText(code, args)));
            return;
        }

        // The name is written at once: the name's unique index decides between this rename and a create or another
        // rename with the same name, and the voucher is used up only for a name that was written.
        string oldName = session.Player.Value.Character.Name;
        refusal = db.RenameCharacter(session.AccountId, session.CharacterId, newName, session.Player.Value.Character);
        if (refusal == NameChangeRules.Refusal.None && !session.Item.Inventory.Consume(item.Uid, 1)) {
            db.RenameCharacter(session.AccountId, session.CharacterId, oldName, session.Player.Value.Character);
            refusal = NameChangeRules.Refusal.Invalid;
        }
        if (refusal != NameChangeRules.Refusal.None) {
            (StringCode code, string[] args) = NameChangeRules.Text(refusal, newName);
            session.Send(NoticePacket.MessageBox(new InterfaceText(code, args)));
            return;
        }
        // The used item is stored with the name, so a crash before the logout save cannot give the item back
        // next to the new name.
        session.SavePurchase(save => session.Item.Inventory.Save(save));

        // KMS2's order: the item, the answer, then the guild's and each club's member name (Guild 12, Club 27, sent
        // by the world's player update to every member online). It sends nothing for the names kept in mails, market
        // listings and the like; those keep the name they were written with.
        session.Player.Value.Character.Name = newName;
        session.Send(CharacterListPacket.NameChanged(session.CharacterId, newName));
        session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Name = newName,
        });
    }

    private static void HandlePremiumClubCoupon(GameSession session, Item item) {
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);

        if (!parameters.ContainsKey("period") || !int.TryParse(parameters["period"], out int hours)) {
            return;
        }

        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }

        session.Config.UpdatePremiumTime(hours);
    }

    // KMS2 answers a box used through RequestItemUse with the inventory packets only (and the gacha script for a
    // gacha box); ItemBox is the answer to RequestItemBox, the multi-open dialog.
    private static void HandleSelectItemBox(GameSession session, IByteReader packet, Item item) {
        session.ItemBox.Open(item, selection: packet.ReadUnicodeString());
        session.ItemBox.Reset();
    }

    private static void HandleOpenItemBox(GameSession session, Item item) {
        session.ItemBox.Open(item);
        session.ItemBox.Reset();
    }

    private static void HandleOpenGacha(GameSession session, IByteReader packet, Item item) {
        string amountString = packet.ReadUnicodeString();
        session.ItemBox.Open(item, amountString == "multi" ? 10 : 1);
        session.ItemBox.Reset();
    }

    private static void HandleOpenItemBoxLullu(GameSession session, IByteReader packet, Item item) {
        string amountString = packet.ReadUnicodeString();
        ItemBoxError error = session.ItemBox.OpenLulluBox(item, amountString.Contains("multi") ? 10 : 1, autoPay: amountString.Contains("autoPay"));
        session.Send(ItemBoxPacket.Open(item.Id, session.ItemBox.BoxCount, error));
        session.ItemBox.Reset();
    }

    private static void HandleOpenItemBoxLulluSimple(GameSession session, IByteReader packet, Item item) {
        string amountString = packet.ReadUnicodeString();
        ItemBoxError error = session.ItemBox.OpenLulluBoxSimple(item, amountString == "multi" ? 10 : 1);
        session.Send(ItemBoxPacket.Open(item.Id, session.ItemBox.BoxCount, error));
        session.ItemBox.Reset();
    }

    private static void HandleItemRepackingScroll(GameSession session, Item item) {
        session.Send(ItemRepackPacket.Open(item.Uid));
    }

    private static void HandleItemChangeBeauty(GameSession session, Item item) {
        session.Send(ItemUsePacket.BeautyCoupon(session.Player.Value.ObjectId, item.Uid));
    }

    private static void HandleInstallBillBoard(GameSession session, IByteReader packet, Item item) {
        string[] fieldParameters = packet.ReadUnicodeString().Split("'");
        if (session.Field == null) {
            return;
        }
        // Every refusal answers the dialog's install banner (PlayerHost mode 7) instead of nothing.
        if (fieldParameters.Length < BillboardRules.InstallTextParts) {
            session.Send(PlayerHostPacket.BillboardInstallResult(BillboardRules.InstallResult(fieldParameters.Length, true, true)));
            return;
        }

        Dictionary<string, string> functionParameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        if (!functionParameters.ContainsKey("interactID") || !int.TryParse(functionParameters["interactID"], out int interactId) ||
            !functionParameters.ContainsKey("durationSec") || !int.TryParse(functionParameters["durationSec"], out int durationSec) ||
            !functionParameters.ContainsKey("model") || !functionParameters.ContainsKey("normal") || !functionParameters.ContainsKey("reactable")) {
            session.Send(PlayerHostPacket.BillboardInstallResult(BillboardRules.InstallResult(fieldParameters.Length, false, true)));
            return;
        }

        string globalId = Guid.NewGuid().ToString();
        var interactMesh = new Ms2InteractMesh(interactId, session.Player.Position, session.Player.Rotation);
        var billboard = new InteractBillBoardObject("BillBoard_" + globalId, interactMesh, session.Player.Value.Character) {
            Title = fieldParameters[0],
            Description = fieldParameters[1],
            PublicHouse = fieldParameters[2].Equals("1"),
            CreationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ExpirationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + durationSec,
            BoardUid = BillboardRules.NextBoardUid(),
            Model = functionParameters["model"],
            Asset = functionParameters.TryGetValue("asset", out string? parameter) ? parameter : string.Empty,
            NormalState = functionParameters["normal"],
            Reactable = functionParameters["reactable"],
            Scale = BillboardRules.Scale(functionParameters.GetValueOrDefault("scale")),
        };

        FieldInteract? fieldInteract = session.Field.AddInteract(interactMesh, billboard);
        if (fieldInteract == null) {
            session.Send(PlayerHostPacket.BillboardInstallResult(BillboardRules.InstallResult(fieldParameters.Length, true, false)));
            return;
        }
        session.ConditionUpdate(ConditionType.install_billboard, targetLong: session.Field.MapId);
        session.Field.Broadcast(InteractObjectPacket.Add(fieldInteract.Object));
        session.Item.Inventory.Consume(item.Uid, 1);
        session.Send(PlayerHostPacket.BillboardInstallResult(BillboardRules.InstallResult(fieldParameters.Length, true, true)));
    }

    private static void HandleOpenMassive(GameSession session, IByteReader packet, Item item) {
        if (session.Field == null) {
            return;
        }
        string password = packet.ReadUnicodeString();
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);

        // weddingParty (the wedding hall items 20001698-20001702) is not handled: no client or data source says what
        // it changes.
        if (!parameters.ContainsKey("fieldID") || !int.TryParse(parameters["fieldID"], out int fieldId) ||
            !parameters.ContainsKey("portalDurationTick") || !int.TryParse(parameters["portalDurationTick"], out int portalDurationTick)) {
            return;
        }

        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            throw new InvalidOperationException($"Failed to consume item: {item.Uid}");
        }
        session.Send(PlayerHostPacket.StartMiniGame(session.PlayerName, fieldId));
        FieldPortal portal = session.Field.SpawnEventPortal(session.Player, fieldId, portalDurationTick, password, OpenMassive.MaxCount(parameters));
        session.Field.UsePortal(session, portal.Value.Id, password);
    }

    private static void HandleDefenseGuard(GameSession session, Item item) {
        int[] npcParameters = item.Metadata.Function?.Parameters.Split(',').Select(int.Parse).ToArray() ?? [];
        if (npcParameters.Length < 3) {
            return;
        }

        int npcId = npcParameters[0];
        int lifeSpanSeconds = npcParameters[1];
        int distance = npcParameters[2];

        if (!session.NpcMetadata.TryGet(npcId, out NpcMetadata? npcMetadata)) {
            return;
        }

        Vector3 position = session.Player.Transform.Position + session.Player.Transform.FrontAxis * distance;
        // TODO: Do we check Z?
        FieldNpc? fieldNpc = session.Field?.SpawnNpc(npcMetadata, position, session.Player.Rotation, playerSummon: true);
        if (fieldNpc == null || session.Field is null) {
            return;
        }
        session.Field.Broadcast(FieldPacket.AddNpc(fieldNpc));
        session.Field.Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
        session.Field.RemoveNpc(fieldNpc.ObjectId, TimeSpan.FromSeconds(lifeSpanSeconds));
        session.Item.Inventory.Consume(item.Uid, 1);
    }

    // A Royale Experience Tonic (SurvivalLevelExp, <v SurvivalExp="N"/>): the client registers no function for it, so
    // UseItem (unit_140d1ae30 @0x140d1bd60) sends the plain use. The tonic is taken and its exp added to the survival
    // level; at the last level (chosen) or without its exp it is kept.
    private static void HandleSurvivalLevelExp(GameSession session, Item item) {
        Dictionary<string, string> xmlParameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        if (!xmlParameters.TryGetValue("SurvivalExp", out string? value) || !long.TryParse(value, out long exp) || exp <= 0) {
            return;
        }
        if (!MapleSurvivalProgress.CanAddItemExp(session) || !session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }
        MapleSurvivalProgress.AddItemExp(session, exp);
    }

    private static void HandleLevelPotion(GameSession session, Item item) {
        Dictionary<string, string> xmlParameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        if (!xmlParameters.ContainsKey("targetLevel") || !short.TryParse(xmlParameters["targetLevel"], out short level)) {
            return;
        }

        int lastClearEpicQuestId = 0;
        if (xmlParameters.TryGetValue("lastClearEpic", out string? lastEpicQuestId) && !int.TryParse(lastEpicQuestId, out lastClearEpicQuestId)) {
            lastClearEpicQuestId = 0;
        }

        // KMS2 takes the potion out of the bag first, then sends the level up.
        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }

        int currentLevel = session.Player.Value.Character.Level;
        session.Player.Value.Character.Level = level;
        session.Field?.Broadcast(LevelUpPacket.LevelUp(session.Player));
        session.Stats.Refresh();

        session.Quest.LevelPotion(level - 1, lastClearEpicQuestId);

        for (int i = currentLevel; i < level; i++) {
            session.ConditionUpdate(ConditionType.level, targetLong: i);
        }

        session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Level = level,
            Async = true,
        });
    }

    // The client checks all of this before it sends the use (CanCreateReactor); the server repeats the event,
    // field, daily count and free cell checks.
    private static void HandleCreateReactor(GameSession session, Item item) {
        if (session.Field == null || !int.TryParse(item.Metadata.Function?.Parameters, out int reactorId)) {
            return;
        }
        if (!session.TableMetadata.ReactorTable.Entries.TryGetValue(reactorId, out ReactorTable.Entry? metadata)) {
            return;
        }
        if (!ReactorEvent.Check(session, metadata, out Maple2.Model.Game.Event.GameEvent? gameEvent)) {
            return;
        }
        // The client counts rewards, not placements, so the event's limit also caps how many reactors
        // of one character stand at once.
        int ownerLimit = gameEvent?.Metadata.Data is Maple2.Model.Metadata.Reactor reactorEvent ? Math.Max(reactorEvent.DailyCount, 1) : 1;
        if (!session.Field.TryAddReactor(metadata, session.Player.Position, session.CharacterId, ownerLimit, out FieldReactor? reactor)) {
            return;
        }
        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            session.Field.RemoveReactor(reactor.ObjectId);
            return;
        }

        // Reactor mode 5 is the client's only use of a reactor's create animation (state 0x3d, sub state 0x9e);
        // chosen here: the placer plays it once the reactor stands.
        session.Send(ReactorPacket.StartReact(metadata.Id));
    }

    // A Blessing Charm (function WeddingExpItem, no parameter): the client has no rule of its own for it and sends the
    // plain use. The marriage's exp log gets the reward table's weddingExpItem exp (350 in KR) in this partner's line,
    // once in 30 days; the charm is taken only then (under the item lock, so the charm the exp was given for is the one
    // taken).
    private static void HandleWeddingExpItem(GameSession session, Item item) {
        lock (session.Item) {
            if (session.Item.Inventory.Get(item.Uid) == null || !session.Marriage.UseWeddingExpItem()) {
                return;
            }
            session.Item.Inventory.Consume(item.Uid, 1);
        }
    }

    // A festival item (FestivalEvent, unit_140d1ae30 FUN_140d1f030): the client sends the plain use only in a field
    // its fieldID lists (0: any), while the game event its gameEvent names runs, and, when its itemUseType is not 0,
    // while a festival of that use type runs. The use opens its dropItem, an individual drop box; the reward is
    // the server's, chosen here: the box's items, the item taken once.
    private static void HandleFestivalEvent(GameSession session, Item item) {
        if (session.Field == null) {
            return;
        }
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        if (!parameters.TryGetValue("dropItem", out string? dropItem) || !int.TryParse(dropItem, out int boxId) ||
            !session.ServerTableMetadata.IndividualDropItemTable.Entries.ContainsKey(boxId)) {
            return;
        }
        int[] fieldIds = parameters.GetValueOrDefault("fieldID", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => int.TryParse(id, out int fieldId) ? fieldId : -1).ToArray();
        int useType = int.TryParse(parameters.GetValueOrDefault("itemUseType"), out int type) ? type : 0;
        bool eventRunning = Enum.TryParse(parameters.GetValueOrDefault("gameEvent"), out GameEventType eventType) && session.FindEvent(eventType).Count > 0;
        int[] festivalTypes = session.FindEvent(GameEventType.Festival)
            .Select(gameEvent => gameEvent.Metadata.Data).OfType<Festival>().Select(festival => festival.UseType).ToArray();
        if (!FestivalUsable(fieldIds, session.Field.MapId, eventRunning, useType, festivalTypes)) {
            return;
        }
        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }
        foreach (Item reward in session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, boxId)) {
            if (!session.Item.Inventory.Add(reward, true)) {
                session.Item.MailItem(reward);
            }
        }
    }

    internal static bool FestivalUsable(int[] fieldIds, int mapId, bool eventRunning, int useType, int[] festivalTypes) =>
        fieldIds.Any(id => id == 0 || id == mapId) && eventRunning && (useType == 0 ? festivalTypes.Length > 0 : festivalTypes.Contains(useType));

    private static void HandleHongBao(GameSession session, Item item) {
        Dictionary<string, string> xmlParameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        if (!xmlParameters.ContainsKey("itemId") || !int.TryParse(xmlParameters["itemId"], out int itemId) ||
            !xmlParameters.ContainsKey("totalCount") || !int.TryParse(xmlParameters["totalCount"], out int totalCount) ||
            !xmlParameters.ContainsKey("totalUser") || !int.TryParse(xmlParameters["totalUser"], out int totalUser) ||
            !xmlParameters.ContainsKey("durationSec") || !int.TryParse(xmlParameters["durationSec"], out int durationSec)) {
            return;
        }
        session.Item.Inventory.Consume(item.Uid, 1);
        session.Field?.AddHongBao(session.Player, item.Id, itemId, totalUser, durationSec, totalCount);
    }

    private void HandleAddAdditionalEffect(GameSession session, Item item) {
        if (session.Field is null) return;
        int[] parameters = item.Metadata.Function?.Parameters.Split(',').Select(int.Parse).ToArray() ?? [];
        if (parameters.Length < 2) {
            return;
        }

        int effectId = parameters[0];
        short effectLevel = (short) parameters[1];
        session.Player.Buffs.AddBuff(session.Player, session.Player, effectId, effectLevel, session.Field.FieldTick, notifyField: true);
        session.Item.Inventory.Consume(item.Uid, 1);
    }

    // An expansion coupon: "rows,tab", the tab by the client's tab name (CItemTabNameTable). The coupon is the whole
    // price; a name the client does not know expands nothing.
    private void HandleExpandInventory(GameSession session, Item item) {
        string[] parameters = item.Metadata.Function?.Parameters.Split(',') ?? [];
        if (parameters.Length < 2 || !int.TryParse(parameters[0], out int amount)) {
            return;
        }

        InventoryType? inventoryType = ExpandInventoryTab(parameters[1]);
        if (inventoryType == null) {
            Logger.Warning("ExpandInven item {ItemId} names no inventory tab: {Parameters}", item.Id, item.Metadata.Function?.Parameters);
            return;
        }

        if (!session.Item.Inventory.Expand(inventoryType.Value, amount, charge: false)) {
            session.Send(ItemUsePacket.MaxInventory());
            return;
        }

        session.Send(ItemUsePacket.ExpandInventory());
        session.Item.Inventory.Consume(item.Uid, 1);
    }

    internal static InventoryType? ExpandInventoryTab(string name) {
        return name.Trim().ToLowerInvariant() switch {
            "game" or "equip" => InventoryType.Gear,
            "skin" => InventoryType.Outfit,
            "summon" => InventoryType.Mount,
            "material" => InventoryType.Catalyst,
            "life" => InventoryType.FishingMusic,
            "quest" => InventoryType.Quest,
            "gem" or "gemstone" => InventoryType.Gemstone,
            "misc" or "etc" => InventoryType.Misc,
            "mastery" => InventoryType.LifeSkill,
            "pet" => InventoryType.Pets,
            "activeskill" => InventoryType.Consumable,
            "coin" => InventoryType.Currency,
            "badge" => InventoryType.Badge,
            "lapenshard" => InventoryType.Lapenshard,
            "piece" => InventoryType.Fragment,
            _ => null,
        };
    }

    private void HandleQuestScroll(GameSession session, Item item) {
        Dictionary<string, string> xmlParameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        if (!xmlParameters.ContainsKey("questID")) {
            Logger.Warning("QuestScroll item {ItemId} is missing questID parameter", item.Id);
            return;
        }

        int[] questIds = xmlParameters["questID"].Split(',').Select(int.Parse).ToArray();
        foreach (int questId in questIds) {
            if (!session.QuestMetadata.TryGet(questId, out QuestMetadata? metadata)) {
                Logger.Warning("QuestScroll item {ItemId} has invalid questID {QuestId}", item.Id, questId);
                return;
            }
            if (!session.Quest.CanStart(metadata)) {
                Logger.Warning("QuestScroll item {ItemId} has questID {QuestId} that cannot be started", item.Id, questId);
                return;
            }
        }

        // A refused quest (its items do not fit the bag) is answered with the quest's own text, and the scroll stays
        // unless one of its quests did start.
        bool started = false;
        foreach (int questId in questIds) {
            QuestError error = session.Quest.Start(questId);
            if (error != QuestError.none) {
                session.Send(QuestPacket.Error(error));
                continue;
            }
            started = true;
        }
        if (!started) {
            return;
        }

        session.Item.Inventory.Consume(item.Uid, 1);
        session.Send(ItemUsePacket.QuestScroll(item.Id));
    }

    // UIPartyDialog onPartyRecall: the party window's recall button casts StateRecall, then uses the scroll.
    private void HandleRecallParty(GameSession session, Item item) {
        if (!session.Party.Recall()) {
            return;
        }

        session.Item.Inventory.Consume(item.Uid, 1);
    }

    // RecallGuild (unnamed/unit_140d1ae30_itemfunctiondispatch.cpp): the client casts StateRecall, then uses the scroll.
    // OpenBank and CallBlackMarket (unit_140d1ae30_itemfunctiondispatch.cpp) start the client's cash call
    // (CCashCallManager @0x140b03d70 / @0x140b03980, cashcallmanager.cpp). At its end StateCashCall::SendRequest sends
    // this plain item use with the price, 0, as text. The client refuses a dead player, and the market also refuses a
    // field whose cashCall market flag is off. The items are 1-slot passes (1, 7 or 30 days, or no end) and are kept.
    // The answer opens the window: StorageInventory 14 (CStore::OnReset, store.cpp:869) opens the bank 0x4e47, which
    // asks for its items itself (StorageInventory Load), and BlackMarket 9 (CBlackMarketMan::OnBlackMarket,
    // blackmarketman.cpp:143) opens the black market 0x4e97.
    private static void HandleOpenBank(GameSession session) {
        if (!CashCallRules.CanOpenBank(session.Player.IsDead)) {
            return;
        }

        session.Send(StorageInventoryPacket.OpenDialog());
    }

    private static void HandleCallBlackMarket(GameSession session) {
        if (session.Field is null || !CashCallRules.CanCallBlackMarket(session.Player.IsDead, session.Field.Metadata.CashCall?.Market == true)) {
            return;
        }

        session.Send(BlackMarketPacket.Open());
    }

    // The medic call (CCashCallManager @0x140b03590, cash call kind 5) cures the death penalty. The client starts it
    // only for a live player with a penalty count above 0 (PC m_n10ec, set by RevivalConfirm) on a field whose cashCall
    // medic flag is on; it sends this item use at the cast's end. The answer is RevivalConfirm with count 0:
    // CSceneStateGame::OnRevivalConfirm (scenestategame.cpp:7356) plays the cure effect when a count above 0 goes to 0.
    // The items are stacks of 999 and one is used per cure.
    private static void HandleCallMedic(GameSession session, Item item) {
        if (session.Field is null) {
            return;
        }
        // Only the count, as the client asks: a run-out penalty keeps its count until the next death or revival, and
        // the client still casts the call then; the cure clears that count.
        bool penalty = session.Config.DeathCount > 0;
        if (!CashCallRules.CanCallMedic(session.Player.IsDead, penalty, session.Field.Metadata.CashCall?.Medic == true)) {
            return;
        }
        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }

        session.Config.UpdateDeathPenalty(0);
    }

    // The Rizeta Stone (ResetAbilityCoolTime, 40100028): the client's confirm box (0xc62, tag use_function_item)
    // sends the plain item use. One stone clears the character ability reset cool time.
    private static void HandleResetAbilityCoolTime(GameSession session, Item item) {
        session.Config.ResetCharacterAbilityCoolTime(() => session.Item.Inventory.Consume(item.Uid, 1));
    }

    // The guild rename ticket (ChangeGuildName): the client's dispatch (@0x140d233d0) opens a name box only for the
    // guild master (else notice 0x416), and its OK (UIMessageBox::HandleChangeGuildNameButton, uimessagebox.cpp:2159)
    // checks the name (not empty, CheckGuildName) and sends the plain item use with the name. The server checks the
    // master and the name as a new guild's, the World renames the guild (refused for a name another guild has), and
    // the ticket is used only then. Each member gets Guild 34, the new name.
    private void HandleChangeGuildName(GameSession session, Item item, IByteReader packet) {
        string name = packet.ReadUnicodeString();
        Guild? guild = session.Guild.Guild;
        if (guild == null) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_null_guild));
            return;
        }
        if (guild.LeaderCharacterId != session.CharacterId) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_master));
            return;
        }
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        if (!GuildRules.IsGuildName(name, constants.GuildNameLengthMin, constants.GuildNameLengthMax) || BanWordStorage.ContainsBannedWord(name)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_name_value));
            return;
        }

        GuildError error;
        try {
            GuildResponse response = session.World.Guild(new GuildRequest {
                RequestorId = session.CharacterId,
                Rename = new GuildRequest.Types.Rename {
                    GuildId = guild.Id,
                    Name = name,
                },
            });
            error = (GuildError) response.Error;
        } catch (RpcException) {
            error = GuildError.s_guild_err_unknown;
        }
        if (error != GuildError.none) {
            session.Send(GuildPacket.Error(error));
            return;
        }
        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            Logger.Warning("Guild {GuildId} renamed, but rename ticket {ItemUid} was not used", guild.Id, item.Uid);
        }
    }

    // The pet skin badge a pet extraction makes: the client's gemPetSkinBaseItemID constant
    // (unit_1402ab440_constantsblocks.cpp:1213), not in the server's constants.
    public const int PetSkinBadgeItemId = 70100000;

    // The pet extraction scroll (PetExtraction @0x140d21a10): the client opens UIPetExtractionScrollDialog. A pet
    // dropped on it shows the badge it gives (FUN_140e71d40, uipetextractionscrolldialog.cpp:325: a pet item whose
    // pet allows extraction, the gemPetSkinBaseItemID badge with that pet id), and the confirmed box sends the
    // scroll's use with the pet's uid as text (OnConfirmUseScroll). The pet is kept: the answer names it, and the
    // client shows its success notice only while it finds the pet. One scroll is used for the new badge.
    private void HandlePetExtraction(GameSession session, Item item, IByteReader packet) {
        if (session.Field is null) {
            return;
        }
        string text = packet.ReadUnicodeString();
        Item? pet = ulong.TryParse(text, out ulong petUid) ? session.Item.Inventory.Get((long) petUid) : null;
        if (pet?.Pet == null || !session.ItemMetadata.TryGetPet(pet.Metadata.Property.PetId, out PetMetadata? petMetadata)
            || !petMetadata.EnableExtraction) {
            session.Send(PetExtractionPacket.Error(PetExtractionError.s_petextraction_invalid));
            return;
        }

        Item? badge = session.Field.ItemDrop.CreateItem(PetSkinBadgeItemId);
        if (badge?.Badge is not { Type: BadgeType.PetSkin }) {
            Logger.Warning("Pet skin badge {ItemId} could not be made", PetSkinBadgeItemId);
            session.Send(PetExtractionPacket.Error(PetExtractionError.s_petextraction_invalid));
            return;
        }
        badge.Badge.PetSkinId = pet.Metadata.Property.PetId;
        if (!session.Item.Inventory.CanAdd(badge)) {
            session.Send(PetExtractionPacket.Error(PetExtractionError.s_petextraction_inventoryfull));
            return;
        }
        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            session.Send(PetExtractionPacket.Error(PetExtractionError.s_petextraction_invalid));
            return;
        }

        Item? added = null;
        if (!session.Item.Inventory.Add(badge, notifyNew: true, commit: true, onAdded: entry => added = entry) || added == null) {
            Logger.Warning("Pet extraction used scroll {ItemUid}, but its badge was not added", item.Uid);
            session.Send(PetExtractionPacket.Error(PetExtractionError.s_petextraction_invalid));
            return;
        }
        session.Send(PetExtractionPacket.Extracted(pet.Uid, added.Uid, pet.Metadata.Property.PetId));
    }

    // The dungeon reward pouch (OpenDungeonItemBox @0x140d24e70): the client opens its confirm box only for a room
    // that exists and has a reward count left, and the confirmed box sends the plain item use. One pouch is used.
    private static void HandleOpenDungeonItemBox(GameSession session, Item item) {
        if (session.Field is null || item.Metadata.Function is null) {
            return;
        }
        DungeonRoomMetadata? room = session.Dungeon.RewardPouchRoom(item.Metadata.Function.Parameters);
        if (room is null || !session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }

        session.Dungeon.OpenRewardPouch(room, session.Field);
    }

    // The pet training kits (PetTraining, 64000001-64000008): the item opens UIItemPetTrainingDialog (0x4f12,
    // uiitempettrainingdialog.cpp), which takes a pet item of pet type 1 below the top level and shows the level and
    // exp the kit's offsetExp gives. Its confirm (after s_msg_transfer_bind_pet_training for an unbound pet with a
    // binding rule) sends the plain item use with the pet's uid as text. The server checks the same, uses one kit,
    // binds the pet and gives the exp as a fusion does (ResponsePet 10, and 11 on a level up), then sends mode 13,
    // which completes the dialog.
    private static void HandlePetTraining(GameSession session, Item item, IByteReader packet) {
        if (!long.TryParse(packet.ReadUnicodeString().Trim(), out long petUid)) {
            return;
        }
        long gain = PetFusionRules.TrainingExp(XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters));
        if (gain <= 0) {
            return;
        }

        lock (session.Item) {
            Item? pet = session.Pet?.Pet.Uid == petUid ? session.Pet.Pet : session.Item.Inventory.Get(petUid, InventoryType.Pets);
            if (pet?.Pet == null || !session.ItemMetadata.TryGetPet(pet.Metadata.Property.PetId, out PetMetadata? petMetadata)) {
                return;
            }
            int maxLevel = session.ServerTableMetadata.ConstantsTable.PetMaxLevel;
            if (!PetFusionRules.IsTrainingTarget(petMetadata.Type, pet.Pet.Level, maxLevel)) {
                return;
            }
            if (!session.Item.Inventory.Consume(item.Uid, 1)) {
                return;
            }
            session.Item.Bind(pet);

            int oldLevel = pet.Pet.Level;
            (int level, long exp) = PetFusionRules.Apply(session.TableMetadata.PetExpTable, pet.Pet.Level, pet.Pet.Exp, pet.Rarity,
                pet.Metadata.Limit.Level, gain, maxLevel);
            pet.Pet.Level = (short) level;
            pet.Pet.Exp = exp;

            session.Send(PetPacket.Fusion(session.Player.ObjectId, pet));
            if (level != oldLevel) {
                session.Send(PetPacket.LevelUp(session.Player.ObjectId, pet));
                if (session.Pet?.Pet.Uid == pet.Uid) {
                    // The summoned pet's stats count in the owner's totals.
                    session.Player.Stats.Refresh();
                }
            }
            session.Send(PetPacket.TrainingComplete());
        }
    }

    private void HandleRecallGuild(GameSession session, Item item) {
        if (!session.Guild.Recall()) {
            return;
        }

        session.Item.Inventory.Consume(item.Uid, 1);
    }

    private void HandleRecallWedding(GameSession session, Item item) {
        if (!session.Marriage.Recall()) {
            return;
        }

        session.Item.Inventory.Consume(item.Uid, 1);
    }

    // The client's ItemActiveCodiTab: the parameter is the number of days, 0 = no end. It asks before sending and
    // refuses by itself when the tab has no end already or the period would pass CodiTabMaxDay.
    private void HandleItemActiveCodiTab(GameSession session, Item item) {
        if (session.Field is null) return;
        if (!int.TryParse(item.Metadata.Function?.Parameters, out int days)) {
            Logger.Warning("ItemActiveCodiTab item {ItemId} has an invalid parameter", item.Id);
            return;
        }

        Character character = session.Player.Value.Character;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!SkinTabPeriod.TryActivate(character.SkinTabStart, character.SkinTabEnd, now, days, Constant.CodiTabMaxDay, out long start, out long end)) {
            return;
        }
        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }

        character.SkinTabStart = start;
        character.SkinTabEnd = end;
        session.Field.Broadcast(ItemUsePacket.ActiveCodiTab(session.Player.ObjectId, start, end, (long) days * SkinTabPeriod.SecondsPerDay));
    }

    // The client's OpenItemBoxLimitedQuantity: an item box with a per-item cooldown between uses
    // (xml parameter cooldownTime, seconds). The client drops an early use without sending it.
    private static void HandleOpenItemBoxLimitedQuantity(GameSession session, Item item) {
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        int cooldownSeconds = parameters.TryGetValue("cooldownTime", out string? cooldown) && int.TryParse(cooldown, out int seconds) ? seconds : 0;
        if (!session.ItemBox.TryStartCooldown(item.Id, cooldownSeconds, Environment.TickCount64)) {
            return;
        }

        ItemBoxError error = session.ItemBox.Open(item);
        session.Send(ItemBoxPacket.Open(item.Id, session.ItemBox.BoxCount, error));
        session.ItemBox.Reset();
    }

    // UIItemPetBoxDialog::SendOpenBox: "single" or "multi" (10 boxes).
    private static void HandleOpenPetBox(GameSession session, IByteReader packet, Item item) {
        string amountString = packet.ReadUnicodeString();
        session.ItemBox.OpenPetBox(item, amountString == "multi" ? 10 : 1);
        session.ItemBox.Reset();
    }

    // UIItemTreasureMapDialog's submit: the item use with "free" when the npc opened the dialog free, else no text. One
    // map leaves the bag, the cost is taken and one roll of every group of the map's drop box is given (the free box
    // 330001824 has more than one group); the answer shows the first item, the dialog has room for one.
    // Without a box, a rolled item, the cost or room for every rolled item, nothing is taken and the dialog goes back
    // to its list.
    private void HandleTreasureMap(GameSession session, IByteReader packet, Item item) {
        bool free = packet.ReadUnicodeString() == TreasureMapRules.FreeArgument;
        if (!TreasureMapRules.TryPick(item.Metadata.Function?.Parameters, free, out int boxId, out long cost)
            || session.Currency.Meso < cost) {
            session.Send(ItemScriptPacket.TreasureMapFailed());
            return;
        }

        ICollection<Item> rewards = session.Field!.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, boxId);
        // The room is checked for the whole roll before the map is taken, so an Add below takes all of it.
        if (rewards.Count == 0 || !session.Item.Inventory.TakesAll(rewards) || !session.Item.Inventory.Consume(item.Uid, 1)) {
            session.Send(ItemScriptPacket.TreasureMapFailed());
            return;
        }

        if (cost > 0) {
            session.Currency.ChangeMeso(-cost);
        }
        foreach (Item reward in rewards) {
            // Only a failed item write gets here (TakesAll checked the bag and furnishing storage); the map is
            // gone, so the reward goes by mail.
            if (!session.Item.AddOrMail(reward, true)) {
                Logger.Error("Treasure map {ItemId} reward {RewardId} x{Amount} was neither added nor mailed to {CharacterId}",
                    item.Id, reward.Id, reward.Amount, session.CharacterId);
            }
        }
        Item shown = rewards.First();
        session.Send(ItemScriptPacket.TreasureMapResult(shown.Id, shown.Rarity));
    }

    // CashAttendEvent: starts the AttendGift event whose item requirement names this item (AttendanceRules.StartByItem)
    // for the requirement's days, and uses up the item. The client gets the start in Attendance 6, which adds it to
    // its map of started events, then Attendance 7 with the event id, which selects the event in the gift window
    // (uiattendgiftdialog.cpp:363). The start and the used item are stored at once in one transaction.
    private void HandleCashAttendEvent(GameSession session, Item item) {
        List<(int Id, AttendGift Data)> events = session.FindEvent(GameEventType.AttendGift)
            .Where(gameEvent => gameEvent.Metadata.Data is AttendGift)
            .Select(gameEvent => (gameEvent.Id, (AttendGift) gameEvent.Metadata.Data))
            .ToList();
        (AttendanceRules.ItemStart result, int eventId) = AttendanceRules.StartByItem(events, item.Id,
            id => session.GameEvent.Peek(GameEventUserValueType.AttendanceItemStartEnd, id) != null);
        switch (result) {
            case AttendanceRules.ItemStart.NotFound:
                session.Send(AttendancePacket.Error(AttendanceError.s_attendGift_item_attend_not_found_evnet));
                return;
            case AttendanceRules.ItemStart.AlreadyStarted:
                session.Send(AttendancePacket.Error(AttendanceError.s_attendGift_item_attend_already_used));
                return;
        }

        GameEvent? gameEvent = session.FindEvent(eventId);
        if (gameEvent?.Metadata.Data is not AttendGift attendGift) {
            return;
        }
        long end = AttendanceRules.ItemStartEnd(DateTime.Now.ToEpochSeconds(), attendGift.Requirement!.Value2, gameEvent.EndTime);
        // The used item and the start are stored at once, together or not at all. Left to the session save, a save
        // of the whole bag before it (a rename, a gender change, a committed add merging into the stack) stored the
        // used item without the start, so a crash then lost both the item and the start.
        var stored = new GameEventUserValue(GameEventUserValueType.AttendanceItemStartEnd, end, eventId);
        stored.SetValue(end.ToString());
        bool taken = false;
        if (!session.TrySavePurchase(db => {
                if (!db.SaveGameEventUserValues(session.CharacterId, [stored])) {
                    return false;
                }
                if (!session.Item.Inventory.SaveConsumed(db, item.Uid, 1)) {
                    return false;
                }
                // Under the account lock, so no session save between the commit and memory writes the old value.
                db.AfterCommit(() => {
                    taken = session.Item.Inventory.ConsumeStored(item.Uid, 1);
                    session.GameEvent.Get(GameEventUserValueType.AttendanceItemStartEnd, eventId, end);
                    session.GameEvent.Set(eventId, GameEventUserValueType.AttendanceItemStartEnd, end);
                });
                return true;
            })) {
            return;
        }
        // Not taken: the item left the bag between its save and the commit (the bag lock is not held across the
        // transaction). The player sees the event started and the reply as usual; the row is stored as spent, but the
        // item where it went keeps its amount in memory, so that place's next save writes the use back and the player
        // keeps the one item. Not closed here; logged so it is seen.
        if (!taken) {
            Logger.Error("CashAttendEvent {ItemUid}: the start was stored but the item was not taken from the bag of {CharacterId}", item.Uid, session.CharacterId);
        }
        session.Send(AttendancePacket.StartedEvents([(eventId, end)]));
        session.Send(AttendancePacket.SelectEvent(eventId));
    }

    // The client's QuestStart: parameter "questId[,level]", level 10 when it is absent or 0. The client checks the
    // level and the quest state, then asks before sending.
    private void HandleQuestStart(GameSession session, Item item) {
        if (!ItemUseParameters.TryParseQuestStart(item.Metadata.Function?.Parameters, out int questId, out short level)) {
            Logger.Warning("QuestStart item {ItemId} has an invalid parameter", item.Id);
            return;
        }
        if (session.Player.Value.Character.Level < level) {
            return;
        }
        if (session.Quest.Start(questId) != QuestError.none) {
            return;
        }

        session.Item.Inventory.Consume(item.Uid, 1);
    }

    private void HandleCallAirTaxi(GameSession session, IByteReader packet, Item item) {
        if (session.Field is null) return;
        string mapIdString = packet.ReadUnicodeString();

        if (!int.TryParse(mapIdString, out int mapId)) {
            Logger.Warning("Invalid map ID: {MapId}", mapIdString);
            return;
        }

        if (!TaxiHandler.CheckMapCashCall(session, mapId, session.Field.MapMetadata)) {
            return;
        }

        bool advanced = item.Metadata.Property.Tag == ItemTag.air_taxi_advanced;
        int tickets = TaxiRules.TicketCost(advanced, session.Player.Value.Unlock.Maps.Contains(mapId));
        if (tickets <= 0) {
            return; // the client refuses this itself (s_err_cash_taxi_need_advanced_ticket, a client-only string)
        }
        bool consumed = tickets == 1
            ? session.Item.Inventory.Consume(item.Uid, 1)
            : session.Item.Inventory.Consume([new IngredientInfo(ItemTag.air_taxi_advanced, tickets)]);
        if (!consumed) {
            session.Send(NoticePacket.MessageBox(StringCode.s_err_lack_itemcount));
            return;
        }

        session.Send(session.PrepareField(mapId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }
}

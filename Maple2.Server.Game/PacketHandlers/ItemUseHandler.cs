using System.Collections.Concurrent;
using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
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

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        long itemUid = packet.ReadLong();

        Item? item = session.Item.Inventory.Get(itemUid);
        if (item == null) {
            Logger.Warning("RequestItemUse for invalid item:{ItemUid}", itemUid);
            return;
        }

        if (item.Metadata.Limit.RequireVip && session.Player.Value.Account.PremiumTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
            return;
        }

        if (item.Metadata.Function?.OnlyShadowWorld == true && session.Field.Metadata.Property.Continent != Continent.ShadowWorld) {
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
            case ItemFunction.CreateReactor:
                HandleCreateReactor(session, item);
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

        long existingTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (session.Player.Value.Unlock.StickerSets.TryGetValue(stickerSetId, out long set)) {
            existingTime = set;
            if (existingTime == long.MaxValue) {
                session.Send(ChatStickerPacket.Error(ChatStickerError.s_msg_chat_emoticon_add_failed_already_exist));
                return;
            }
        }

        long newTime = existingTime + duration;
        if (duration == 0) {
            newTime = long.MaxValue;
        }

        if (newTime <= existingTime) {
            session.Send(ChatStickerPacket.Error(ChatStickerError.s_msg_chat_emoticon_add_failed_already_exist));
            return;
        }

        if (!session.Item.Inventory.Consume(item.Uid, 1)) {
            return;
        }

        session.Player.Value.Unlock.StickerSets[stickerSetId] = newTime;

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
        if (!session.Field.TryAddReactor(metadata, session.Player.Position, session.CharacterId, ownerLimit, out _)) {
            return;
        }

        session.Item.Inventory.Consume(item.Uid, 1);
    }

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

        foreach (int questId in questIds) {
            session.Quest.Start(questId);
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

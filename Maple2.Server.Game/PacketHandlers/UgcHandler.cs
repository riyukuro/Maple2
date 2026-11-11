using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Ugc;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;


namespace Maple2.Server.Game.PacketHandlers;

public class UgcHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Ugc;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WebStorage WebStorage { private get; init; }
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required WorldClient World { private get; init; }
    // ReSharper restore All
    #endregion

    private enum Command : byte {
        UploadFailed = 0,
        Upload = 1,
        Confirmation = 3,
        ProfilePicture = 11,
        Unknown12 = 12,
        LoadBanners = 18,
        ReserveBanner = 19,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.UploadFailed:
                // One byte. See UgcFeeBook: the fee of this character's paid, unfinished upload goes
                // back once; with nothing pending the request does nothing.
                GiveBack(session, session.UgcFees.TakeBack(session.CharacterId));
                return;
            case Command.Upload:
                HandleUpload(session, packet);
                return;
            case Command.Confirmation:
                HandleConfirmation(session, packet);
                return;
            case Command.ProfilePicture:
                HandleProfilePicture(session, packet);
                return;
            case Command.LoadBanners:
                HandleLoadBanners(session);
                return;
            case Command.ReserveBanner:
                HandleReserveBanner(session, packet);
                return;
            case Command.Unknown12:
                // Not handled: one path string.
                Logger.Debug("Ugc {Command} from {CharacterId}: {Path}", command, session.CharacterId, packet.ReadUnicodeString());
                return;
        }
    }

    private void HandleUpload(GameSession session, IByteReader packet) {
        // CUgcManager::SendUGCMode1: the item uid of the upload control, the upload head, then the data of the kind.
        packet.ReadLong();
        UgcUploadHeader info = UgcUploadHeader.Read(packet);

        // The client has one upload at a time: a paid one that never finished is given back before
        // the next is charged, so a retry costs the fee once.
        GiveBack(session, session.UgcFees.TakeBack(session.CharacterId));

        switch (info.Type) {
            case UgcType.Item:
            case UgcType.Furniture:
            case UgcType.Mount:
                UploadItem(session, packet, info.Type);
                return;
            case UgcType.Banner:
                UploadBanner(session, packet);
                return;
            case UgcType.GuildEmblem:
                UploadGuildEmblem(session, packet);
                return;
            case UgcType.GuildBanner:
                UploadGuildBanner(session, packet);
                return;
            case UgcType.LayoutBlueprint:
                UploadLayoutBlueprint(session, packet);
                return;
            default:
                Logger.Information("Unimplemented Ugc Type: {type}", info.Type);
                return;
        }
    }

    // Gives a fee back and drops what the upload staged: the item, and on this field the banner
    // designs of the upload that have no picture (their slots stay reserved for the character).
    private void GiveBack(GameSession session, UgcPendingFee? fee) {
        if (fee == null) {
            return;
        }

        if (session.StagedUgcItem?.Template?.Id == fee.UgcUid) {
            session.StagedUgcItem = null;
        }
        if (session.Field != null && fee.UgcUid != 0) {
            foreach (BannerSlot slot in session.Field.Banners.Values.SelectMany(banner => banner.Slots)) {
                if (slot.Template?.Id == fee.UgcUid && slot.Template.CharacterId == session.CharacterId && string.IsNullOrEmpty(slot.Template.Url)) {
                    slot.Template = null;
                }
            }
        }

        if (fee.VoucherItemId != 0) {
            Item? voucher = session.Field?.ItemDrop.CreateItem(fee.VoucherItemId);
            if (voucher == null || !session.Item.Inventory.Add(voucher, true)) {
                Logger.Error("Failed to give back design coupon {ItemId} to {CharacterId}", fee.VoucherItemId, session.CharacterId);
            }
            return;
        }
        switch (fee.Currency) {
            case MeretMarketCurrencyType.Meso:
                session.Currency.Meso += fee.Amount;
                break;
            case MeretMarketCurrencyType.Meret:
                session.Currency.Meret += fee.Amount;
                break;
            case MeretMarketCurrencyType.RedMeret:
                session.Currency.GameMeret += fee.Amount;
                break;
        }
    }

    private void UploadItem(GameSession session, IByteReader packet, UgcType ugcType) {
        long itemUid = packet.ReadLong();
        int itemId = packet.ReadInt();
        int amount = packet.ReadInt();
        string name = packet.ReadUnicodeString();
        packet.ReadByte();
        long cost = packet.ReadLong();
        bool useVoucher = packet.ReadBool();

        if (!TableMetadata.UgcDesignTable.Entries.TryGetValue(itemId, out UgcDesignTable.Entry? ugcMetadata)) {
            return;
        }

        if (session.Item.Inventory.FreeSlots(InventoryType.Outfit) <= 0) {
            return;
        }

        // The voucher byte is the client's. Every path below either takes a coupon or the full price, or returns.
        long price = ugcMetadata.CreatePrice;
        if (price < 0) {
            return;
        }
        Item? voucher = useVoucher
            ? session.Item.Inventory.Filter(item => item.Metadata.Property.Tag == ItemTag.FreeDesignCoupon).FirstOrDefault()
            : null;
        var paid = new UgcPendingFee(session.CharacterId, 0, ugcMetadata.CurrencyType, price, 0);
        bool canPay = ugcMetadata.CurrencyType switch {
            MeretMarketCurrencyType.Meso => session.Currency.CanAddMeso(-price) == -price,
            MeretMarketCurrencyType.Meret => session.Currency.CanAddMeret(-price) == -price,
            MeretMarketCurrencyType.RedMeret => session.Currency.CanAddGameMeret(-price) == -price,
            _ => false,
        };
        switch (UgcUploadPayment.Decide(useVoucher, voucher != null, ugcMetadata.CurrencyType, canPay)) {
            case UgcUploadPayment.Result.Voucher:
                if (!session.Item.Inventory.Consume(voucher!.Uid, 1)) {
                    session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_err_invalid_item));
                    return;
                }
                paid = paid with { VoucherItemId = voucher.Id };
                break;
            case UgcUploadPayment.Result.Pay:
                switch (ugcMetadata.CurrencyType) {
                    case MeretMarketCurrencyType.Meso:
                        session.Currency.Meso -= price;
                        break;
                    case MeretMarketCurrencyType.Meret:
                        session.Currency.Meret -= price;
                        break;
                    case MeretMarketCurrencyType.RedMeret:
                        session.Currency.GameMeret -= price;
                        break;
                    default:
                        return;
                }
                break;
            case UgcUploadPayment.Result.NoVoucher:
                session.Send(NoticePacket.Notice(NoticePacket.Flags.Alert | NoticePacket.Flags.Message, StringCode.s_err_invalid_item));
                return;
            case UgcUploadPayment.Result.Lack:
                session.Send(NoticePacket.MessageBox(ugcMetadata.CurrencyType switch {
                    MeretMarketCurrencyType.Meso => StringCode.s_err_lack_meso,
                    MeretMarketCurrencyType.Meret => StringCode.s_err_lack_merat,
                    _ => StringCode.s_err_lack_merat_red,
                }));
                return;
            default:
                Logger.Error("Unhandled currency type {UgcMetadataCurrencyType}", ugcMetadata.CurrencyType);
                return;
        }

        Item? item = session.Field?.ItemDrop.CreateItem(itemId, ugcMetadata.ItemRarity);
        if (item == null) {
            GiveBack(session, paid);
            Logger.Fatal("Failed to create UGC item {ItemId}", itemId);
            throw new InvalidOperationException($"Fatal: Creating UGC item: {itemId}");
        }

        using WebStorage.Request request = WebStorage.Context();
        UgcResource? resource = request.CreateUgc(ugcType, session.CharacterId);
        if (resource == null) {
            GiveBack(session, paid);
            Logger.Fatal("Failed to create UGC resource for item {ItemUid}", item.Uid);
            throw new InvalidOperationException($"Fatal: Creating UGC resource: {item.Uid}");
        }

        item.Template = new UgcItemLook {
            Id = resource.Id,
            AccountId = session.AccountId,
            Author = session.PlayerName,
            CharacterId = session.CharacterId,
            CreationTime = DateTime.Now.ToEpochSeconds(),
            Name = name,
        };

        session.StagedUgcItem = item;
        session.UgcFees.Paid(paid with { UgcUid = resource.Id });
        session.Send(UgcPacket.Upload(resource));
    }

    private void UploadBanner(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        long bannerId = packet.ReadLong();

        session.Field.Banners.TryGetValue(bannerId, out FieldUgcBanner? banner);
        if (banner is null) {
            Logger.Warning("Failed to find banner {BannerId}", bannerId);
            return;
        }

        BannerTable.Entry? bannerMetadata = TableMetadata.BannerTable.Entries.FirstOrDefault(x => x.Id == bannerId);
        if (bannerMetadata is null) {
            Logger.Warning("Failed to find banner metadata {BannerId}", bannerId);
            return;
        }

        List<BannerSlot> slots = [];
        List<(long SlotId, bool Found, bool Taken, int Hour)> asked = [];

        // Every slot is read and tested before any meret is taken: the hour priced is the reserved
        // slot's own, a slot is open only to the character that reserved it, and one refused slot
        // refuses the upload with nothing charged.
        byte hours = packet.ReadByte();
        for (int i = 0; i < hours; i++) {
            var reservation = packet.Read<UgcBannerReservation>();
            BannerSlot? bannerSlot = banner.Slots.FirstOrDefault(slot => slot.Id == reservation.Uid);
            UgcItemLook? shown = bannerSlot?.Template;
            bool taken = bannerSlot == null || !UgcRules.BannerSlotOpenTo(bannerSlot.OwnerId, shown?.CharacterId ?? 0,
                !string.IsNullOrEmpty(shown?.Url), session.CharacterId);
            asked.Add((reservation.Uid, bannerSlot != null, taken, bannerSlot?.Hour ?? -1));
            if (bannerSlot != null) {
                slots.Add(bannerSlot);
            }
        }

        long fee = UgcRules.BannerUploadFee(bannerMetadata.Price, asked);
        if (fee < 0) {
            Logger.Warning("Refused banner upload {BannerId} from {CharacterId}", bannerId, session.CharacterId);
            return;
        }
        if (session.Currency.Meret < fee) {
            session.Send(NoticePacket.MessageBox(StringCode.s_err_lack_merat));
            return;
        }

        // The resource is made first, so a failure here takes nothing.
        using WebStorage.Request request = WebStorage.Context();
        UgcResource? resource = request.CreateUgc(UgcType.Banner, session.CharacterId);
        if (resource == null) {
            Logger.Fatal("Failed to create UGC resource for banner {BannerId}", bannerId);
            throw new InvalidOperationException($"Fatal: Creating UGC resource: {bannerId}");
        }
        session.Currency.Meret -= fee;
        session.UgcFees.Paid(new UgcPendingFee(session.CharacterId, resource.Id, MeretMarketCurrencyType.Meret, fee, 0));

        UgcItemLook ugc = new() {
            Id = resource.Id,
            AccountId = session.AccountId,
            Author = session.PlayerName,
            CharacterId = session.CharacterId,
            CreationTime = DateTime.Now.ToEpochSeconds(),
            Name = $"AD Banner {bannerId}",
        };

        foreach (BannerSlot slot in slots) {
            slot.Template = ugc;
            slot.OwnerId = session.CharacterId;

            BannerSlot oldSlot = banner.Slots.First(x => x.Id == slot.Id);
            banner.Slots.Remove(oldSlot);
            banner.Slots.Add(slot);
        }

        session.Send(UgcPacket.Upload(resource));
    }

    private void UploadGuildBanner(GameSession session, IByteReader packet) {
        (long guildId, int bannerId) = UgcRules.ReadGuildPoster(packet);

        if (session.Guild.Guild is null) {
            Logger.Warning("Failed to find guild for UGC {GuildId}", guildId);
            return;
        }

        if (!UgcRules.OwnGuild(guildId, session.Guild.Id)) {
            Logger.Warning("Invalid guild for UGC {GuildId}", guildId);
            return;
        }

        // The client tests no rank right for a poster (CUgcManager::OpenGuildPosterUpload: a guild, and
        // the poster object of the guild house). The id is the object's housePosterId.
        if (!UgcRules.GuildPosterIdValid(bannerId, TableMetadata.InteractObjectTable.Entries.Values.Select(entry => entry.GuildPosterId))) {
            Logger.Warning("Invalid guild poster {PosterId} from {CharacterId}", bannerId, session.CharacterId);
            return;
        }

        using WebStorage.Request request = WebStorage.Context();
        UgcResource? resource = request.CreateUgc(UgcType.GuildBanner, session.CharacterId);
        if (resource == null) {
            Logger.Fatal("Failed to create UGC resource for banner {BannerId}", bannerId);
            throw new InvalidOperationException($"Fatal: Creating UGC resource: {bannerId}");
        }

        session.StagedGuildPoster = new GuildPoster {
            Id = bannerId,
            OwnerId = session.CharacterId,
            OwnerName = session.PlayerName,
            ResourceId = resource.Id,
        };

        session.Send(UgcPacket.Upload(resource));
    }

    private void UploadGuildEmblem(GameSession session, IByteReader packet) {
        long guildId = UgcRules.ReadGuildMark(packet);
        if (session.Guild.Guild is null) {
            Logger.Error("Cannot upload guild emblem without a guild");
            return;
        }

        if (!UgcRules.OwnGuild(guildId, session.Guild.Id)) {
            Logger.Warning("Invalid guild for UGC {GuildId}", guildId);
            return;
        }

        if (!session.Guild.HasPermission(session.CharacterId, GuildPermission.EditEmblem)) {
            session.Send(GuildPacket.Error(GuildError.s_guild_err_no_authority));
            return;
        }

        using WebStorage.Request request = WebStorage.Context();
        UgcResource? resource = request.CreateUgc(UgcType.GuildEmblem, session.CharacterId);
        if (resource == null) {
            Logger.Fatal("Failed to create UGC resource for guild id {GuildId}", session.Guild.Id);
            throw new InvalidOperationException($"Fatal: Creating UGC resource: {session.Guild.Id}");
        }

        session.StagedGuildEmblemUgc = resource.Id;
        session.Send(UgcPacket.Upload(resource));
    }

    private void UploadLayoutBlueprint(GameSession session, IByteReader packet) {
        long blueprintUid = packet.ReadLong();
        long itemUid = packet.ReadLong();
        int itemId = packet.ReadInt();
        string name = packet.ReadUnicodeString();

        Item? item = session.StagedUgcItem;
        if (item is null || item.Uid != itemUid || item.Id != itemId) {
            Logger.Error("Failed to find staged item for UGC {ItemUid} {ItemId}", itemUid, itemId);
            return;
        }

        using WebStorage.Request request = WebStorage.Context();
        UgcResource? resource = request.CreateUgc(UgcType.LayoutBlueprint, session.CharacterId);
        if (resource == null) {
            Logger.Fatal("Failed to create UGC resource for layout blueprint for character {CharacterId}", session.CharacterId);
            throw new InvalidOperationException($"Fatal: Creating UGC resource for layout blueprint for character: {session.CharacterId}");
        }

        item.Template = new UgcItemLook {
            Id = resource.Id,
            AccountId = session.AccountId,
            Author = session.PlayerName,
            CharacterId = session.CharacterId,
            CreationTime = DateTime.Now.ToEpochSeconds(),
            Name = name,
        };

        session.StagedUgcItem = item;
        session.Send(UgcPacket.Upload(resource));
    }

    private void HandleConfirmation(GameSession session, IByteReader packet) {
        UgcUploadHeader info = UgcUploadHeader.Read(packet);
        long ugcUid = info.UgcUid;
        string ugcGuid = info.UgcName;

        if (info.AccountId != session.AccountId || info.CharacterId != session.CharacterId || ugcUid == 0) {
            Logger.Warning("Invalid UGC confirmation {AccountId} {CharacterId} {UgcUid}", info.AccountId, info.CharacterId, ugcUid);
            return;
        }

        using WebStorage.Request webRequest = WebStorage.Context();
        UgcResource? resource = webRequest.GetUgc(ugcUid);
        if (resource == null) {
            Logger.Warning("Failed to find UGC resource {UgcUid}", ugcUid);
            return;
        }

        // True for the upload that mode 1 charged; after it a mode 0 gives nothing back.
        bool paidUpload = session.UgcFees.Finish(session.CharacterId, ugcUid);

        switch (info.Type) {
            case UgcType.Item or UgcType.Furniture or UgcType.Mount:
                ConfirmItem();
                break;
            case UgcType.Banner:
                ConfirmBanner();
                break;
            case UgcType.GuildBanner:
                ConfirmGuildBanner();
                break;
            case UgcType.GuildEmblem:
                ConfirmGuildEmblem();
                break;
            case UgcType.LayoutBlueprint:
                ConfirmLayoutBlueprint();
                break;
            default:
                Logger.Warning("Unhandled Confirmation for UGC Type {UgcType}", info.Type);
                break;
        }

        session.StagedUgcItem = null;
        session.StagedGuildPoster = null;
        session.StagedGuildEmblemUgc = 0;
        return;

        void ConfirmGuildBanner() {
            Guild? guild = session.Guild.Guild;
            if (guild is null) {
                Logger.Warning("Failed to find guild for UGC {UgcUid}", ugcUid);
                return;
            }

            if (session.StagedGuildPoster is null || !UgcRules.GuildConfirmAllowed(session.StagedGuildPoster.ResourceId, ugcUid, resource.Type == UgcType.GuildBanner)) {
                Logger.Warning("Failed to find guild poster for UGC {UgcUid}", ugcUid);
                return;
            }

            try {
                var request = new GuildRequest {
                    RequestorId = session.CharacterId,
                    UpdatePoster = new GuildRequest.Types.UpdatePoster {
                        GuildId = session.Guild.Id,
                        Id = session.StagedGuildPoster.Id,
                        Picture = resource.Path,
                        OwnerId = session.StagedGuildPoster.OwnerId,
                        OwnerName = session.StagedGuildPoster.OwnerName,
                        ResourceId = session.StagedGuildPoster.ResourceId,
                    },
                };

                GuildResponse response = World.Guild(request);
                var error = (GuildError) response.Error;
                if (error != GuildError.none) {
                    session.Send(GuildPacket.Error(error));
                    return;
                }
            } catch (RpcException) { /* ignored */
            }
        }

        void ConfirmGuildEmblem() {
            Guild? guild = session.Guild.Guild;
            if (guild is null) {
                Logger.Warning("Failed to find guild for UGC {UgcUid}", ugcUid);
                return;
            }

            // Only the emblem upload this session started: any other resource id is not made the emblem.
            if (!UgcRules.GuildConfirmAllowed(session.StagedGuildEmblemUgc, ugcUid, resource.Type == UgcType.GuildEmblem)) {
                Logger.Warning("Failed to find guild emblem upload for UGC {UgcUid}", ugcUid);
                return;
            }

            try {
                var request = new GuildRequest {
                    RequestorId = session.CharacterId,
                    UpdateEmblem = new GuildRequest.Types.UpdateEmblem {
                        GuildId = session.Guild.Id,
                        Emblem = resource.Path,
                    },
                };

                GuildResponse response = World.Guild(request);
                var error = (GuildError) response.Error;
                if (error != GuildError.none) {
                    session.Send(GuildPacket.Error(error));
                    return;
                }
            } catch (RpcException) { /* ignored */
            }
        }

        void ConfirmBanner() {
            // Only the charged upload of this character: one that was given back shows nothing.
            if (!paidUpload) {
                Logger.Warning("Banner confirmation without a paid upload {UgcUid}", ugcUid);
                return;
            }
            UgcBanner? banner = session.Field?.Banners.Values.FirstOrDefault(x => x.Slots.Any(slot => slot.Template?.Id == ugcUid && slot.Template.CharacterId == session.CharacterId));
            if (banner is null) {
                Logger.Warning("Failed to find banner for UGC {UgcUid}", ugcUid);
                return;
            }

            using (GameStorage.Request db = session.GameStorage.Context()) {
                foreach (BannerSlot slot in banner.Slots.Where(slot => slot.Template?.Id == ugcUid)) {
                    slot.Template!.Url = resource.Path;
                    db.UpdateBannerSlot(slot);
                }
            }

            session.Send(UgcPacket.UpdateBanner(banner));
        }

        void ConfirmItem() {
            Item? item = session.StagedUgcItem;
            if (item?.Template == null || item.Template.Id != ugcUid || !paidUpload || !TableMetadata.UgcDesignTable.Entries.TryGetValue(item.Id, out UgcDesignTable.Entry? ugcMetadata)) {
                return;
            }

            item.Template.Url = resource.Path;
            long uid;

            // Dont create the item if it's a furniture since it's tied to account id
            if (info.Type is UgcType.Furniture) {
                uid = session.Item.Furnishing.AddCube(item);
            } else {
                using GameStorage.Request db = session.GameStorage.Context();
                Item? newAdd = db.CreateItem(session.CharacterId, item);
                if (newAdd == null) {
                    Logger.Error("Failed to create UGC item {ItemId}", item.Id);
                    return;
                }

                uid = newAdd.Uid;
                session.Item.Inventory.Add(newAdd, notifyNew: true);
            }

            session.Send(UgcPacket.UpdateItem(session.Player.ObjectId, uid, item, ugcMetadata.CreatePrice, info.Type));
            session.Send(UgcPacket.UpdatePath(resource));
        }

        void ConfirmLayoutBlueprint() {
            Item? item = session.StagedUgcItem;
            if (item?.Template is null || item.Blueprint is null) {
                return;
            }

            item.Template.Url = resource.Path;

            using GameStorage.Request gameRequest = session.GameStorage.Context();
            if (!gameRequest.UpdateItem(item)) {
                Logger.Fatal("Failed to update UGC Item {ugcUid}", ugcUid);
                throw new InvalidOperationException($"Fatal: UGC Item update: {ugcUid}");
            }

            session.Send(UgcPacket.UpdateLayoutBlueprint(session.Player.ObjectId, item));
            session.Send(UgcPacket.UpdatePath(resource));
        }
    }

    private static void HandleProfilePicture(GameSession session, IByteReader packet) {
        string path = packet.ReadUnicodeString();
        session.Player.Value.Character.Picture = path;

        session.ConditionUpdate(ConditionType.change_profile);
        session.Field?.Broadcast(UgcPacket.ProfilePicture(session.Player));
    }

    private static void HandleLoadBanners(GameSession session) {
        if (session.Field is null) return;
        session.Send(UgcPacket.LoadBanners(session.Field.Banners.Values.Select(fieldUgcBanner => (UgcBanner) fieldUgcBanner).ToList()));
    }

    private void HandleReserveBanner(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        long bannerId = packet.ReadLong();
        int hours = packet.ReadInt();

        if (hours is < 0 or > 24) {
            return;
        }

        session.Field.Banners.TryGetValue(bannerId, out FieldUgcBanner? banner);
        if (banner is null) {
            Logger.Warning("Failed to find banner {BannerId}", bannerId);
            return;
        }

        List<BannerSlot> newSlots = [];

        // Reserving is free, so it is bounded: the slots this character holds on the field without
        // a design, and the ones of this request, stay within UgcRules.BannerReserveMax.
        int room = UgcRules.BannerReserveRoom(session.Field.Banners.Values.Sum(other =>
            other.Slots.Count(slot => slot.OwnerId == session.CharacterId && slot.Template == null && !slot.Expired)));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using GameStorage.Request db = session.GameStorage.Context();
        for (int i = 0; i < hours; i++) {
            var reservation = packet.Read<UgcBannerReservation>();

            // The slot's start time is built from the date and the hour: values that are no time, or an
            // hour that is over, are skipped.
            if (!UgcRules.BannerTimeOpen(reservation.Date, reservation.Hour, now)) {
                continue;
            }
            if (room <= 0) {
                continue;
            }

            BannerSlot? held = banner.Slots.FirstOrDefault(slot => slot.Date == reservation.Date && slot.Hour == reservation.Hour);
            if (held != null && UgcRules.BannerSlotClaimable(held.OwnerId, held.Template != null)) {
                // Reserved before a restart: the owner was lost, so the first to ask for the hour again holds it.
                held.OwnerId = session.CharacterId;
                room--;
                newSlots.Add(held);
                continue;
            }
            if (held != null) {
                Logger.Warning("Failed to reserve banner slot {BannerId} {Date} {Hour}", bannerId, reservation.Date, reservation.Hour);
                continue;
            }

            BannerSlot slot = new(banner.Id, reservation.Date, reservation.Hour);
            slot = db.AddBannerSlot(slot);
            slot.OwnerId = session.CharacterId;
            room--;
            newSlots.Add(slot);
            banner.Slots.Add(slot);
        }

        session.Send(UgcPacket.ReserveBannerSlots(bannerId, newSlots));
    }
}

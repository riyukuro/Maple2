using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Ugc;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Tools.Extensions;

// ReSharper disable RedundantTypeArgumentsOfMethod

namespace Maple2.Server.Core.Packets;

public static class UgcPacket {
    private enum Command : byte {
        Upload = 2,
        UpdatePath = 4,
        EnableBanner = 7,
        UpdateBanner = 8,
        ProfilePicture = 11,
        UpdateItem = 13,
        UpdateFurnishing = 14,
        UpdateMount = 15,
        UpdateLayoutBlueprint = 16,
        SetEndpoint = 17,
        LoadBanner = 18,
        ReserveBanners = 20,
        CostTable = 21,
    }

    public static ByteWriter Upload(UgcResource ugc) {
        var pWriter = Packet.Of(SendOp.Ugc);
        pWriter.Write<Command>(Command.Upload);
        pWriter.Write<UgcType>(ugc.Type);
        pWriter.WriteLong(ugc.Id);
        pWriter.WriteUnicodeString(ugc.Id.ToString());

        return pWriter;
    }

    public static ByteWriter UpdatePath(UgcResource ugc) {
        var pWriter = Packet.Of(SendOp.Ugc);
        pWriter.Write<Command>(Command.UpdatePath);
        pWriter.Write<UgcType>(ugc.Type);
        pWriter.WriteLong(ugc.Id);
        pWriter.WriteUnicodeString(ugc.Path);

        return pWriter;
    }

    // CUgcManager::OnUgc mode 0x11: the SOAP url, the download host and region (FUN_1410268a0, where only
    // "cn" changes anything: the download cache layout), then the SOAP mode (CSoapClient::FUN_14176fcf0).
    // The mode is not a locale: 2 keeps the url's path (the url less "ws.asmx?wsdl"), any other value
    // clears it, so it stays 2 under every LOCALE.
    public static ByteWriter SetEndpoint(Uri uri) {
        var pWriter = Packet.Of(SendOp.Ugc);
        pWriter.Write<Command>(Command.SetEndpoint);
        pWriter.WriteUnicodeString($"{uri.Scheme}://{uri.Authority}/ws.asmx?wsdl");
        pWriter.WriteUnicodeString($"{uri.Scheme}://{uri.Authority}");
        pWriter.WriteUnicodeString(Target.LOCALE.ToLower());
        pWriter.WriteByte(2);

        return pWriter;
    }

    public static ByteWriter ProfilePicture(Player player) {
        var pWriter = Packet.Of(SendOp.Ugc);
        pWriter.Write<Command>(Command.ProfilePicture);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteLong(player.Character.Id);
        pWriter.WriteUnicodeString(player.Character.Picture);

        return pWriter;
    }

    public static ByteWriter ProfilePicture(Character character) {
        var pWriter = Packet.Of(SendOp.Ugc);
        pWriter.Write<Command>(Command.ProfilePicture);
        pWriter.WriteInt();
        pWriter.WriteLong(character.Id);
        pWriter.WriteUnicodeString(character.Picture);

        return pWriter;
    }

    public static ByteWriter UpdateItem(int objectId, long uid, Item item, long createPrice, UgcType ugcType) {
        var pWriter = Packet.Of(SendOp.Ugc);
        switch (ugcType) {
            case UgcType.Item:
                pWriter.Write<Command>(Command.UpdateItem);
                break;
            case UgcType.Furniture:
                pWriter.Write<Command>(Command.UpdateFurnishing);
                break;
            case UgcType.Mount:
                pWriter.Write<Command>(Command.UpdateMount);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(ugcType), ugcType, null);
        }

        pWriter.WriteInt(objectId);

        pWriter.WriteLong(uid);
        pWriter.WriteInt(item.Id);
        pWriter.WriteInt(item.Amount);
        pWriter.WriteUnicodeString(item.Template!.Name);
        pWriter.WriteByte(1);
        pWriter.WriteLong(createPrice);
        pWriter.WriteByte();

        pWriter.WriteClass<UgcItemLook>(item.Template);

        return pWriter;
    }

    public static ByteWriter UpdateLayoutBlueprint(int objectId, Item item) {
        var pWriter = Packet.Of(SendOp.Ugc);
        pWriter.Write<Command>(Command.UpdateLayoutBlueprint);
        pWriter.WriteInt(objectId);
        pWriter.WriteLong(item.Blueprint!.BlueprintUid);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteInt(item.Id);
        pWriter.WriteUnicodeString(item.Template!.Name);
        pWriter.WriteClass<UgcItemLook>(item.Template);

        return pWriter;
    }

    public static ByteWriter LoadBanners(List<UgcBanner> banners) {
        var pWriter = Packet.Of(SendOp.Ugc);
        pWriter.Write<Command>(Command.LoadBanner);

        int counter1 = 0;
        pWriter.WriteInt(counter1);
        for (int i = 0; i < counter1; i++) {
            bool flagA = false;
            pWriter.WriteBool(flagA); // CUgcBannerRollingImage
            if (flagA) {
                pWriter.WriteLong();
                pWriter.WriteUnicodeString();
                pWriter.WriteByte();
                pWriter.WriteInt();
                pWriter.WriteLong();
                pWriter.WriteLong();
                pWriter.WriteUnicodeString();
                pWriter.WriteUnicodeString();
                pWriter.WriteUnicodeString();
            }
        }

        pWriter.WriteInt(banners.Count);
        foreach (UgcBanner ugcBanner in banners) {
            pWriter.WriteLong(ugcBanner.Id);
            BannerSlot? activeSlot = ugcBanner.SlotsSnapshot().FirstOrDefault(x => x.Active);
            pWriter.WriteBool(activeSlot?.Template is not null);
            if (activeSlot?.Template is null) {
                continue;
            }

            pWriter.WriteActiveBannerSlot(activeSlot);
        }

        pWriter.WriteInt(banners.Count);
        foreach (UgcBanner ugcBanner in banners) {
            pWriter.WriteUgcBanner(ugcBanner.Id, ugcBanner.SlotsSnapshot());
        }

        return pWriter;
    }

    public static ByteWriter ActivateBanner(UgcBanner ugcBanner) {
        var pWriter = Packet.Of(SendOp.Ugc);
        pWriter.Write<Command>(Command.EnableBanner);
        pWriter.WriteLong(ugcBanner.Id);
        BannerSlot? activeSlot = ugcBanner.SlotsSnapshot().FirstOrDefault(x => x.Active);
        pWriter.WriteBool(activeSlot?.Template is not null);
        if (activeSlot?.Template is null) {
            return pWriter;
        }

        pWriter.WriteActiveBannerSlot(activeSlot);

        return pWriter;
    }

    public static ByteWriter UpdateBanner(UgcBanner ugcBanner) {
        var pWriter = Packet.Of(SendOp.Ugc);
        pWriter.Write<Command>(Command.UpdateBanner);
        pWriter.WriteUgcBanner(ugcBanner.Id, ugcBanner.SlotsSnapshot());

        return pWriter;
    }

    public static ByteWriter ReserveBannerSlots(long bannerId, List<BannerSlot> bannerSlots) {
        var pWriter = Packet.Of(SendOp.Ugc);
        pWriter.Write<Command>(Command.ReserveBanners);
        pWriter.WriteLong(bannerId);
        pWriter.WriteInt(bannerSlots.Count);
        foreach (BannerSlot slot in bannerSlots) {
            pWriter.WriteClass<BannerSlot>(slot);
        }

        return pWriter;
    }

    // CUgcManager::OnUgc mode 0x15 (CUgcGetCostTable::Read): a count, then a long step and an int value per row.
    // The client lists the rows (step + 1, 100 - value) in the tooltips of the design market's register and
    // settle windows, which show no tooltip without the table.
    public static ByteWriter CostTable(IReadOnlyList<(long Step, int Value)> rows) {
        var pWriter = Packet.Of(SendOp.Ugc);
        pWriter.Write<Command>(Command.CostTable);
        pWriter.WriteInt(rows.Count);
        foreach ((long step, int value) in rows) {
            pWriter.WriteLong(step);
            pWriter.WriteInt(value);
        }

        return pWriter;
    }

    private static void WriteUgcBanner(this ByteWriter pWriter, long bannerId, List<BannerSlot> banners) {
        pWriter.WriteLong(bannerId);
        pWriter.WriteInt(banners.Count);
        foreach (BannerSlot bannerSlot in banners) {
            long bannerSlotDate = long.Parse($"{bannerSlot.Date}00000") + bannerSlot.Hour; // yes. this is stupid. Who approved this?
            pWriter.WriteLong(bannerSlotDate);
            pWriter.WriteUnicodeString(bannerSlot.Template?.Author ?? string.Empty);
            pWriter.WriteBool(true); //  true = reserved, false = awaiting reservation, not sure when false is used
        }
    }

    private static void WriteActiveBannerSlot(this ByteWriter pWriter, BannerSlot activeSlot) {
        pWriter.Write(UgcType.Banner);
        pWriter.WriteInt(2);
        pWriter.WriteLong(activeSlot.Template!.AccountId);
        pWriter.WriteLong(activeSlot.Template.CharacterId);
        pWriter.WriteUnicodeString(); // unknown
        pWriter.WriteUnicodeString(activeSlot.Template.Author);
        pWriter.WriteLong(activeSlot.Template.Id);
        pWriter.WriteUnicodeString(activeSlot.Template.Id.ToString());
        pWriter.WriteByte(3);
        pWriter.WriteByte(1);
        pWriter.WriteUnicodeString(); // upload path
        pWriter.WriteLong(activeSlot.BannerId);
        byte loopCounter = 1; // Usually it's all slots but only one can be active at a time, so why have more than one?
        pWriter.WriteByte(loopCounter);
        for (byte i = 0; i < loopCounter; i++) {
            pWriter.WriteClass<BannerSlot>(activeSlot);
        }

        pWriter.WriteUnicodeString(activeSlot.Template.Url);
    }
}

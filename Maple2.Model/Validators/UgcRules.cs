using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// Ugc 0x0038, the banner modes. Mode 19 reserves hours of a banner: i64 banner id, i32 count, then per
// hour i64, i32, i64, i32 date (yyyymmdd), i32 hour, i64. Mode 1 with the banner kind then pays for the
// reserved slots: i64 banner id, u8 count and the same records, the first i64 being the slot's id.
public static class UgcRules {
    // The hour of a day and a date that exists; the slot's start time is built from them.
    public static bool BannerTimeValid(int date, int hour) {
        if (hour is < 0 or > 23) {
            return false;
        }
        int year = date / 10000;
        int month = date % 10000 / 100;
        int day = date % 100;
        return year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month);
    }

    // The slot's hour has not ended yet (slot times are UTC, as the banner's own clock).
    public static bool BannerTimeOpen(int date, int hour, DateTimeOffset now) {
        if (!BannerTimeValid(date, hour)) {
            return false;
        }
        var start = new DateTimeOffset(date / 10000, date % 10000 / 100, date % 100, hour, 0, 0, TimeSpan.Zero);
        return start.AddHours(1) > now;
    }

    // Mode 19 costs nothing, and the client sends the hours of one day in one request (24 at most).
    // Server rule: a character holds at most this many reserved slots that it has not paid for.
    public const int BannerReserveMax = 24;

    // How many more slots the character may reserve.
    public static int BannerReserveRoom(int unpaidOwned) {
        return Math.Clamp(BannerReserveMax - unpaidOwned, 0, BannerReserveMax);
    }

    // Whether the character may upload to a reserved slot. The slot belongs to the character that
    // reserved it (ownerId, 0 when not known: the owner is kept in memory only). A design on the slot
    // names its owner too: a finished one closes the slot, one without a picture (an upload that
    // failed) may be replaced by its own character.
    public static bool BannerSlotOpenTo(long ownerId, long designCharacterId, bool hasPicture, long characterId) {
        if (characterId == 0 || (ownerId != 0 && ownerId != characterId)) {
            return false;
        }
        if (designCharacterId != 0) {
            return designCharacterId == characterId && !hasPicture;
        }
        return ownerId == characterId;
    }

    // Mode 1, guild emblem (CUgcGuildMarkProxy::Write): i64 guild id.
    public static long ReadGuildMark(IByteReader packet) {
        return packet.ReadLong();
    }

    // Mode 1, guild poster (CUgcGuildPosterProxy::Write): i64 guild id, i32 the poster object's id.
    public static (long GuildId, int PosterId) ReadGuildPoster(IByteReader packet) {
        long guildId = packet.ReadLong();
        int posterId = packet.ReadInt();
        return (guildId, posterId);
    }

    // The guild named by the client is the sender's own. The client fills in its own guild id and
    // does not start the upload without one.
    public static bool OwnGuild(long sentGuildId, long guildId) {
        return guildId != 0 && sentGuildId == guildId;
    }

    // A guild poster goes on a poster object of the guild house: the id is one of the table's.
    public static bool GuildPosterIdValid(int posterId, IEnumerable<int> posterIds) {
        return posterId > 0 && posterIds.Contains(posterId);
    }

    // Mode 3 of a guild kind finishes the upload this session started with mode 1, and the resource
    // is of that kind.
    public static bool GuildConfirmAllowed(long stagedUgcUid, long ugcUid, bool resourceOfKind) {
        return ugcUid != 0 && stagedUgcUid == ugcUid && resourceOfKind;
    }

    // The price of one hour of the banner, -1 when the table has none for it.
    public static long BannerPrice(IReadOnlyList<long> prices, int hour) {
        if (hour < 0 || hour >= prices.Count || prices[hour] < 0) {
            return -1;
        }
        return prices[hour];
    }

    // The fee of an upload over its slots, or -1 when the upload is refused whole: a slot that is not
    // reserved, one that is not open to the sender (BannerSlotOpenTo), one named twice, or an hour without a price. Nothing
    // is taken before every slot has passed.
    public static long BannerUploadFee(IReadOnlyList<long> prices, IReadOnlyList<(long SlotId, bool Found, bool Taken, int Hour)> slots) {
        if (slots.Count == 0) {
            return -1;
        }
        var seen = new HashSet<long>();
        long total = 0;
        foreach ((long slotId, bool found, bool taken, int hour) in slots) {
            long price = BannerPrice(prices, hour);
            if (!found || taken || price < 0 || !seen.Add(slotId)) {
                return -1;
            }
            total += price;
        }
        return total;
    }
}

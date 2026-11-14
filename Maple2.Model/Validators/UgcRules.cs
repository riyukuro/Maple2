using System.Globalization;
using Maple2.Model.Enum;
using Maple2.Model.Game.Ugc;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// Ugc 0x0038, the banner modes. Mode 19 reserves hours of a banner: i64 banner id, i32 count, then per
// hour i64, i32, i64, i32 date (yyyymmdd), i32 hour, i64. Mode 1 with the banner kind then pays for the
// reserved slots: i64 banner id, u8 count and the same records, the first i64 being the slot's id.
public static class UgcRules {
    // The upload the game server opens with its Upload reply (UgcPacket.Upload). The client posts the files at once on
    // that reply (CUgcManager::OnUploadAccepted, ugcmanager_4.cpp:3554: PrepareUpload, then UploadPendingUgc, up to
    // 3 tries 500 ms apart, soapclient.cpp:318-333). Chosen here: the web server takes them for 5 minutes.
    public static readonly TimeSpan UploadWindow = TimeSpan.FromMinutes(5);
    // The files of an upload, each taken once: the file itself, and the icon a design (ItemIcon) or a blueprint
    // (BlueprintIcon) sends before it (UploadPendingUgc :114-116).
    public const byte UploadFile = 1;
    public const byte UploadIcon = 2;

    public static byte UploadPart(UgcType kind) => kind is UgcType.ItemIcon or UgcType.BlueprintIcon ? UploadIcon : UploadFile;

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

    // The banner's live slot is the one of the current hour (date and hour, UTC); every other slot is not active.
    // Before, a slot went live when its day of the month and hour matched (a slot of the 5th of next month went
    // live on the 5th of this one), and a live slot stayed live until it was deleted 4 hours after its start, so
    // the first of them in the list (LoadBanners, ActivateBanner) showed over the later hours bought after it.
    // Returns whether a flag changed; the caller then sends the banner's live slot (none restores the default
    // image, CUgcManager::FUN_141887dc0). Call under lock (slots).
    public static bool SetLiveBannerSlot(List<BannerSlot> slots, DateTimeOffset now) {
        DateTimeOffset utc = now.ToUniversalTime();
        int date = utc.Year * 10000 + utc.Month * 100 + utc.Day;
        bool changed = false;
        foreach (BannerSlot slot in slots) {
            bool live = !slot.Expired && slot.Date == date && slot.Hour == utc.Hour;
            if (slot.Active != live) {
                slot.Active = live;
                changed = true;
            }
        }
        return changed;
    }

    // The live slot when it has a design whose picture arrived (its url set by the confirmation); a design
    // still waiting for its picture is not sent, as the client would download an empty url.
    public static BannerSlot? LiveDesign(IEnumerable<BannerSlot> slots) {
        BannerSlot? live = slots.FirstOrDefault(slot => slot.Active);
        return live?.Template is { Url: { Length: > 0 } } ? live : null;
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
    // Server rule: a character holds at most this many reserved slots without a finished design
    // (BannerSlotHeld).
    public const int BannerReserveMax = 24;

    // How many more slots the character may reserve.
    public static int BannerReserveRoom(int unpaidOwned) {
        return Math.Clamp(BannerReserveMax - unpaidOwned, 0, BannerReserveMax);
    }

    // Whether a slot counts against BannerReserveMax for the character: it owns the slot, the hour is
    // not over, and no finished design is on it. A design without a picture (an upload that was
    // charged and not finished, or one that failed) counts like no design.
    public static bool BannerSlotHeld(long ownerId, bool hasPicture, bool expired, long characterId) {
        return characterId != 0 && ownerId == characterId && !hasPicture && !expired;
    }

    // Takes the designs of one upload off the slots it was staged on, when they have no picture. The
    // slots are the ones the upload named, on whatever field they are; they stay reserved for their
    // owner. Returns how many were cleared.
    public static int ClearStagedBanner(IEnumerable<BannerSlot> staged, long ugcUid, long characterId) {
        if (ugcUid == 0) {
            return 0;
        }
        int cleared = 0;
        foreach (BannerSlot slot in staged) {
            if (slot.Template?.Id == ugcUid && slot.Template.CharacterId == characterId && string.IsNullOrEmpty(slot.Template.Url)) {
                slot.Template = null;
                cleared++;
            }
        }
        return cleared;
    }

    // A reserved slot whose owner was lost (kept in memory only: 0 after a restart, after the field
    // was disposed, and on another channel) and that has
    // no design may be reserved again; the one who asks first holds it. An owned slot or one with a
    // design is never given to another.
    public static bool BannerSlotClaimable(long ownerId, bool hasDesign) {
        return ownerId == 0 && !hasDesign;
    }

    // Mode 19, the part that runs under the lock of the banner's slot list: each asked hour that is
    // open and free is taken while room is left. An hour whose owner was lost is claimed; a free hour
    // gets a new slot, added to the list at once so that no other request can take the hour. Nothing
    // is written to the database here: the new slots (created) have no id yet, the caller writes them
    // after the lock and takes a slot whose write failed back out with ReleaseBannerSlot.
    // Returns the slots the character got, claimed and new, in the order asked.
    public static List<BannerSlot> ReserveBannerSlots(List<BannerSlot> slots, long bannerId, IEnumerable<(int Date, int Hour)> asked,
                                                      long characterId, int room, DateTimeOffset now, out List<BannerSlot> created) {
        var got = new List<BannerSlot>();
        created = [];
        lock (slots) {
            foreach ((int date, int hour) in asked) {
                if (room <= 0 || !BannerTimeOpen(date, hour, now)) {
                    continue;
                }

                BannerSlot? held = slots.FirstOrDefault(slot => slot.Date == date && slot.Hour == hour);
                if (held != null) {
                    if (!BannerSlotClaimable(held.OwnerId, held.Template != null)) {
                        continue;
                    }
                    held.OwnerId = characterId;
                    room--;
                    got.Add(held);
                    continue;
                }

                var slot = new BannerSlot(bannerId, date, hour) { OwnerId = characterId };
                slots.Add(slot);
                created.Add(slot);
                got.Add(slot);
                room--;
            }
        }
        return got;
    }

    // A slot of ReserveBannerSlots that could not be written is taken out of the list again.
    public static void ReleaseBannerSlot(List<BannerSlot> slots, BannerSlot slot) {
        lock (slots) {
            slots.Remove(slot);
        }
    }

    // The new slots of ReserveBannerSlots are written one by one, each by its own call of write (the
    // caller gives each its own database context: a failed insert stays tracked in the context it was
    // tried in and would fail every later write there). A slot whose write throws is taken out of the
    // list and out of got; the others stay. Returns the slots that failed.
    public static List<BannerSlot> SaveReservedSlots(List<BannerSlot> slots, List<BannerSlot> got, List<BannerSlot> created,
                                                     Action<BannerSlot> write, Action<BannerSlot, Exception>? failed = null) {
        var lost = new List<BannerSlot>();
        foreach (BannerSlot slot in created) {
            try {
                write(slot);
            } catch (Exception ex) {
                failed?.Invoke(slot, ex);
                ReleaseBannerSlot(slots, slot);
                got.Remove(slot);
                lost.Add(slot);
            }
        }
        return lost;
    }

    // A slot has its id from the database. Between ReserveBannerSlots and its write a new slot is in
    // the list with id 0: it is not a slot an upload can name (uid 0 names none) and has no row to remove.
    public static bool BannerSlotSaved(long id) => id != 0;

    public static BannerSlot? FindSavedBannerSlot(IEnumerable<BannerSlot> slots, long uid) {
        return BannerSlotSaved(uid) ? slots.FirstOrDefault(slot => slot.Id == uid) : null;
    }

    // Whether the character may upload to a reserved slot. The slot belongs to the character that
    // reserved it (ownerId, 0 when not known: the owner is kept in memory only). A slot with no owner
    // and no design takes an upload from anyone, at the usual price: the owner is lost whenever the
    // field is disposed, and the client does not reserve an hour it holds a second time. A design on
    // the slot names its owner too: a finished one closes the slot, one without a picture (an upload
    // that failed) may be replaced by its own character.
    public static bool BannerSlotOpenTo(long ownerId, long designCharacterId, bool hasPicture, long characterId) {
        if (characterId == 0 || (ownerId != 0 && ownerId != characterId)) {
            return false;
        }
        if (designCharacterId != 0) {
            return designCharacterId == characterId && !hasPicture;
        }
        return true;
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

    // The sales window offers "stop the sale" (MeretMarket 15) only for a listing on sale, and "delete"
    // (MeretMarket 14) only for one that is not on sale (it shows a notice for one on sale and sends nothing).
    public static bool ListingStopAllowed(UgcMarketListingStatus status) {
        return status == UgcMarketListingStatus.Active;
    }

    public static bool ListingDeleteAllowed(UgcMarketListingStatus status) {
        return status != UgcMarketListingStatus.Active;
    }

    // A layout blueprint's preview: only the upload this session staged on the blueprint, and a resource of
    // that kind.
    public static bool BlueprintConfirmAllowed(long stagedUgcUid, long ugcUid, bool resourceOfKind) {
        return ugcUid != 0 && stagedUgcUid == ugcUid && resourceOfKind;
    }

    // The name of a design (item, furniture, mount): the design window gives its text field UgcNameLengthMax
    // (14 today; uiugcitemeditdialog.cpp ugcNameLengthMax). UgcNameLengthMin is only put in the hint text there,
    // so no lower limit is asked. No control characters; a limit of 0 is not tested.
    public static bool DesignNameValid(string? name, int max) {
        if (name == null || (max > 0 && name.Length > max)) {
            return false;
        }
        return !name.Any(char.IsControl);
    }

    // Mode 11 names the picture the web server stored for the character's profile upload: the path it
    // answered, data/profiles/avatar/<character id>/<file>.png. Any other text is not made the picture
    // (it is shown to every player that sees the character).
    public static bool ProfilePathValid(string? path, long characterId) {
        string prefix = $"data/profiles/avatar/{characterId}/";
        const string suffix = ".png";
        if (path == null || characterId <= 0 || path.Length <= prefix.Length + suffix.Length || path.Length > 128 ||
            !path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(suffix, StringComparison.Ordinal)) {
            return false;
        }
        string file = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
        return file.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-');
    }

    // The character a profile picture path names (data/profiles/avatar/<character id>/...), 0 when it names none. The
    // login scene's mode 11 carries only the path: the character list sends it for the character whose picture was
    // uploaded (CUgcManager upload result, UICharacterListDialog), whichever of the account's characters that is.
    public static long ProfilePathCharacter(string? path) {
        const string prefix = "data/profiles/avatar/";
        if (path == null || !path.StartsWith(prefix, StringComparison.Ordinal)) {
            return 0;
        }
        int end = path.IndexOf('/', prefix.Length);
        // ProfilePathValid then compares the whole path with the one built from the id (no sign, no leading zero).
        return end > prefix.Length && long.TryParse(path.AsSpan(prefix.Length, end - prefix.Length), NumberStyles.None,
            CultureInfo.InvariantCulture, out long characterId) ? characterId : 0;
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

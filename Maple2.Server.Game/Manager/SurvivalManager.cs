using System.Collections.Concurrent;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class SurvivalManager {
    private readonly GameSession session;

    private readonly ILogger logger = Log.Logger.ForContext<SurvivalManager>();
    private readonly ConcurrentDictionary<MedalType, Dictionary<int, Medal>> inventory;
    private readonly ConcurrentDictionary<MedalType, Medal> equip;

    private int SurvivalLevel {
        get => session.Player.Value.Account.SurvivalLevel;
        set => session.Player.Value.Account.SurvivalLevel = value;
    }

    private long SurvivalExp {
        get => session.Player.Value.Account.SurvivalExp;
        set => session.Player.Value.Account.SurvivalExp = value;
    }

    private int SurvivalSilverLevelRewardClaimed {
        get => session.Player.Value.Account.SurvivalSilverLevelRewardClaimed;
        set => session.Player.Value.Account.SurvivalSilverLevelRewardClaimed = value;
    }

    private int SurvivalGoldLevelRewardClaimed {
        get => session.Player.Value.Account.SurvivalGoldLevelRewardClaimed;
        set => session.Player.Value.Account.SurvivalGoldLevelRewardClaimed = value;
    }

    private bool ActiveGoldPass {
        get => session.Player.Value.Account.ActiveGoldPass;
        set => session.Player.Value.Account.ActiveGoldPass = value;
    }


    public SurvivalManager(GameSession session) {
        this.session = session;
        inventory = new ConcurrentDictionary<MedalType, Dictionary<int, Medal>>();
        equip = new ConcurrentDictionary<MedalType, Medal>();
        foreach (MedalType type in Enum.GetValues<MedalType>()) {
            equip[type] = new Medal(0, type);
            inventory[type] = new Dictionary<int, Medal>();
        }
        using GameStorage.Request db = session.GameStorage.Context();
        List<Medal> medals = db.GetMedals(session.CharacterId);

        foreach (Medal medal in medals) {
            if (!inventory.TryGetValue(medal.Type, out Dictionary<int, Medal>? dict)) {
                dict = new Dictionary<int, Medal>();
                inventory[medal.Type] = dict;
            }

            dict[medal.Id] = medal;
            if (medal.Slot != -1) {
                equip[medal.Type] = medal;
            }
        }
    }

    // The medal held of that type and id, if any.
    public Medal? Find(MedalType type, int id) {
        return inventory.TryGetValue(type, out Dictionary<int, Medal>? medals) && medals.TryGetValue(id, out Medal? medal) ? medal : null;
    }

    // A medal item's skin (<v id="N" type="effectTail|riding|gliding" durationSec="S"|endDate="yyyy-MM-dd-HH-mm-ss"/>):
    // its id and type, and its end (an end date, now plus the duration, or never). False when the id or the type is
    // missing or unknown.
    private bool TryParseMedal(Item item, out int id, out MedalType type, out long expiryTime, out bool byDuration) {
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        type = default;
        expiryTime = Never;
        byDuration = false;
        if (!parameters.TryGetValue("id", out string? idStr) || !int.TryParse(idStr, out id)) {
            id = 0;
            logger.Warning("Failed to add medal: missing or invalid ID parameter");
            return false;
        }

        if (!parameters.TryGetValue("type", out string? typeStr)) {
            logger.Warning("Failed to add medal: missing or invalid type parameter");
            return false;
        }

        MedalType? parsed = typeStr switch {
            "effectTail" => MedalType.Tail,
            "gliding" => MedalType.Gliding,
            "riding" => MedalType.Riding,
            _ => null,
        };
        if (parsed == null) {
            logger.Warning("Failed to add medal {Id}: invalid type {Type}", id, typeStr);
            return false;
        }
        type = parsed.Value;

        // Get expiration
        if (parameters.TryGetValue("durationSec", out string? durationStr) && int.TryParse(durationStr, out int durationSec)) {
            byDuration = true;
            expiryTime = Now() + durationSec;
        } else if (parameters.TryGetValue("endDate", out string? endDateStr) && DateTime.TryParseExact(endDateStr, "yyyy-MM-dd-HH-mm-ss", null, System.Globalization.DateTimeStyles.None, out DateTime endDate)) {
            //2018-10-02-00-00-00
            expiryTime = endDate.ToEpochSeconds();
        }
        return true;
    }

    private static readonly long Never = DateTime.MaxValue.ToEpochSeconds() - 1;

    private static long Now() => (long) (DateTime.Now.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;

    // The end of a medal held (ending at held) that a second one renews: a duration adds its time to what is left of
    // it (from now once it has expired), an end date or a lasting medal keeps the later end. Before, the new end itself
    // (seconds since 1970) was added, so a renewed medal never expired.
    private static long Renewed(long held, long expiryTime, bool byDuration) {
        long now = Now();
        return expiryTime == Never ? Never
            : byDuration ? Math.Min(Math.Max(held, now) + (expiryTime - now), Never)
            : Math.Max(held, expiryTime);
    }

    public void AddMedal(Item item) {
        if (!TryParseMedal(item, out int id, out MedalType type, out long expiryTime, out bool byDuration)) {
            return;
        }

        if (inventory[type].TryGetValue(id, out Medal? existing)) {
            existing.ExpiryTime = Renewed(existing.ExpiryTime, expiryTime, byDuration);
            session.Send(SurvivalPacket.LoadMedals(inventory, equip));
            return;
        }

        Medal? medal = CreateMedal(id, type, expiryTime);
        if (medal == null) {
            return;
        }

        if (!inventory.TryGetValue(medal.Type, out Dictionary<int, Medal>? dict)) {
            dict = new Dictionary<int, Medal>();
            inventory[medal.Type] = dict;
        }

        dict[medal.Id] = medal;

        // The restored client gets the new skin alone (0x1f, the end sent as the medal list sends it) and the learned
        // line (0x21); before, the whole list was sent again and no line was shown. The KR client keeps the list.
        if (MapleSurvivalRules.RestoredClient) {
            session.Send(MapleSurvivalPacket.SkinAdded((byte) medal.Type, medal.Id, medal.ExpiryTime));
            session.Send(MapleSurvivalPacket.SkinLearned((byte) medal.Type, medal.Id));
        } else {
            session.Send(SurvivalPacket.LoadMedals(inventory, equip));
        }
    }

    // A medal item used from the bag (SurvivalSkin: the client registers no use function for it, so UseItem,
    // unit_140d1ae30 @0x140d1bd60, sends the plain use). Such a row reaches the bag only past InventoryManager.Add,
    // which learns a medal item instead of placing it (a row stored before that, or placed by a load). The medal (new,
    // or the one held renewed as AddMedal renews it) and the used item are stored in one transaction
    // (InventoryManager.SaveConsumed); the medal reaches memory and the item leaves the bag once it committed, so a
    // crash cannot keep the medal and give the item back or the other way round. False when nothing was stored: the
    // skin is unknown (not in the skin table), the bag does not hold the item, or the store failed; the item is kept.
    public bool UseMedal(Item item) {
        if (!TryParseMedal(item, out int id, out MedalType type, out long expiryTime, out bool byDuration)) {
            return false;
        }
        if (!session.TableMetadata.SurvivalSkinInfoTable.Entries.TryGetValue(id, out MedalType known) || known != type) {
            logger.Warning("Medal item {ItemId}: skin {Id} ({Type}) is not in the skin table", item.Id, id, type);
            return false;
        }

        Medal? existing = Find(type, id);
        long end = existing == null ? expiryTime : Renewed(existing.ExpiryTime, expiryTime, byDuration);
        Medal? created = null;
        bool taken = true;
        bool stored = session.TrySavePurchase(db => {
            if (existing != null) {
                // The renewed end goes to the row; memory keeps the old one until the commit.
                if (!db.SaveMedals(session.CharacterId, new Medal(id, type) { Slot = existing.Slot, ExpiryTime = end })) {
                    return false;
                }
            } else {
                created = db.CreateMedal(session.CharacterId, new Medal(id, type) { ExpiryTime = end });
                if (created == null) {
                    return false;
                }
            }
            if (!session.Item.Inventory.SaveConsumed(db, item.Uid, 1)) {
                return false;
            }
            db.AfterCommit(() => {
                if (existing != null) {
                    existing.ExpiryTime = end;
                } else if (created != null) {
                    inventory[type][id] = created;
                }
                taken = session.Item.Inventory.ConsumeStored(item.Uid, 1);
            });
            return true;
        });
        if (!stored) {
            logger.Error("Medal item {ItemUid} ({Id}) of {CharacterId} not stored, the item is kept", item.Uid, id, session.CharacterId);
            return false;
        }
        if (!taken) {
            logger.Error("Medal item {ItemUid} stored as used but not taken from the bag of {CharacterId}", item.Uid, session.CharacterId);
        }

        if (existing == null && created != null && MapleSurvivalRules.RestoredClient) {
            session.Send(MapleSurvivalPacket.SkinAdded((byte) created.Type, created.Id, created.ExpiryTime));
            session.Send(MapleSurvivalPacket.SkinLearned((byte) created.Type, created.Id));
        } else {
            session.Send(SurvivalPacket.LoadMedals(inventory, equip));
        }
        return true;
    }

    public bool Equip(MedalType type, int id) {
        if (!Enum.IsDefined(type)) {
            return false;
        }

        if (id == 0) {
            Unequip(type);
            session.Send(SurvivalPacket.LoadMedals(inventory, equip));
            return true;
        }

        if (!inventory[type].TryGetValue(id, out Medal? medal)) {
            return false;
        }

        // An ended medal is refused, as the restored client refuses it (s_item_err_puton_expired).
        if (Ended(medal)) {
            return false;
        }

        // medal is already equipped
        if (medal.Slot != -1) {
            return false;
        }

        // unequip existing medal
        if (equip[type].Id != 0) {
            Medal equipped = equip[type];
            equipped.Slot = -1;
        }

        equip[type] = medal;
        medal.Slot = (short) type;

        session.Send(SurvivalPacket.LoadMedals(inventory, equip));
        return true;
    }

    private static bool Ended(Medal medal) {
        return medal.ExpiryTime < DateTime.UtcNow.ToEpochSeconds();
    }

    private void Unequip(MedalType type) {
        Medal medal = equip[type];
        equip[type] = new Medal(0, type);
        medal.Slot = -1;
    }

    private Medal? CreateMedal(int id, MedalType type, long expiryTime) {
        var medal = new Medal(id, type) {
            ExpiryTime = expiryTime,
        };

        using GameStorage.Request db = session.GameStorage.Context();
        return db.CreateMedal(session.CharacterId, medal);
    }

    public void Load() {
        // An equipped medal whose end has passed comes off (chosen here: the medal stays in the list, ended).
        foreach ((MedalType type, Medal medal) in equip.ToArray()) {
            if (medal.Id != 0 && Ended(medal)) {
                Unequip(type);
            }
        }
        // The stats are the open period's: the KR client's entry sends them without the lobby, which enters it too.
        MapleSurvivalProgress.EnterPeriod(session);
        session.Send(SurvivalPacket.UpdateStats(session.Player.Value.Account));
        session.Send(SurvivalPacket.LoadMedals(inventory, equip));
    }

    public void Save(GameStorage.Request db) {
        var medals = inventory.Values.SelectMany(dict => dict.Values).ToArray();
        db.SaveMedals(session.CharacterId, medals);
    }
}

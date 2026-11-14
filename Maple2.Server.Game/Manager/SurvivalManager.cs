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

    public void AddMedal(Item item) {
        Dictionary<string, string> parameters = XmlParseUtil.GetParameters(item.Metadata.Function?.Parameters);
        if (!parameters.TryGetValue("id", out string? idStr) || !int.TryParse(idStr, out int id)) {
            logger.Warning("Failed to add medal: missing or invalid ID parameter");
            return;
        }

        if (!parameters.TryGetValue("type", out string? typeStr)) {
            logger.Warning("Failed to add medal: missing or invalid type parameter");
            return;
        }

        MedalType type = typeStr switch {
            "effectTail" => MedalType.Tail,
            "gliding" => MedalType.Gliding,
            "riding" => MedalType.Riding,
            _ => throw new InvalidOperationException($"Invalid medal type: {typeStr}"),
        };

        long never = DateTime.MaxValue.ToEpochSeconds() - 1;
        long now = (long) (DateTime.Now.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;
        long expiryTime = never;
        bool byDuration = false;
        // Get expiration
        if (parameters.TryGetValue("durationSec", out string? durationStr) && int.TryParse(durationStr, out int durationSec)) {
            byDuration = true;
            expiryTime = now + durationSec;
        } else if (parameters.TryGetValue("endDate", out string? endDateStr) && DateTime.TryParseExact(endDateStr, "yyyy-MM-dd-HH-mm-ss", null, System.Globalization.DateTimeStyles.None, out DateTime endDate)) {
            //2018-10-02-00-00-00
            expiryTime = endDate.ToEpochSeconds();
        }

        // Check if medal already exists: a duration adds its time to what is left of it (from now once it has
        // expired), an end date or a lasting medal keeps the later end. Before, the new end itself (seconds since
        // 1970) was added, so a renewed medal never expired.
        if (inventory[type].TryGetValue(id, out Medal? existing)) {
            existing.ExpiryTime = expiryTime == never ? never
                : byDuration ? Math.Min(Math.Max(existing.ExpiryTime, now) + (expiryTime - now), never)
                : Math.Max(existing.ExpiryTime, expiryTime);
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
        session.Send(SurvivalPacket.UpdateStats(session.Player.Value.Account));
        session.Send(SurvivalPacket.LoadMedals(inventory, equip));
    }

    public void Save(GameStorage.Request db) {
        var medals = inventory.Values.SelectMany(dict => dict.Values).ToArray();
        db.SaveMedals(session.CharacterId, medals);
    }
}

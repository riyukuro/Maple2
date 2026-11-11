using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Util;

// Whether a player's chosen name-tag symbol (insignia) is shown. The client tests the same conditions
// on the wearer's side only (CNametagSymbolTable::CheckCondition) and asks again with an Insignia
// packet when its answer differs from the flag the server sent; other clients draw the symbol from
// the flag alone (field add and the Insignia update).
public static class InsigniaRules {
    public readonly record struct Facts(bool HasTitle, int PrestigeLevel, int TrophyTotal, bool HighEnchant, int Level, bool Vip, bool Gm);

    public static bool Shown(InsigniaConditionType type, in Facts facts) {
        return type switch {
            InsigniaConditionType.title => facts.HasTitle,
            InsigniaConditionType.adventure_level => facts.PrestigeLevel >= 100,
            InsigniaConditionType.trophy_point => facts.TrophyTotal >= 1000,
            InsigniaConditionType.enchant => facts.HighEnchant,
            InsigniaConditionType.level => facts.Level >= 50,
            InsigniaConditionType.vip => facts.Vip,
            InsigniaConditionType.GM => facts.Gm, // the client: GM grade 50 or more
            _ => false,
        };
    }

    // Insignia (c2s 0x006B): i16 id. The entry is null when the table does not hold the id.
    public static short ReadId(IByteReader packet, IReadOnlyDictionary<int, InsigniaTable.Entry> entries, out InsigniaTable.Entry? entry) {
        short id = packet.ReadShort();
        entries.TryGetValue(id, out entry);
        return id;
    }

    public static bool Handled(InsigniaConditionType type) {
        return type is InsigniaConditionType.title or InsigniaConditionType.adventure_level or InsigniaConditionType.trophy_point
            or InsigniaConditionType.enchant or InsigniaConditionType.level or InsigniaConditionType.vip or InsigniaConditionType.GM;
    }

    public static bool Shown(GameSession session, InsigniaTable.Entry entry) {
        return Shown(entry.Type, new Facts(
            HasTitle: session.Player.Value.Unlock.HasTitle(entry.Code),
            PrestigeLevel: session.Player.Value.Account.PrestigeLevel,
            TrophyTotal: session.Player.Value.Character.AchievementInfo.Total,
            HighEnchant: session.Item.Equips.Gear.Any(item => item.Value.Enchant?.Enchants >= 12 && item.Value.Rarity > 3),
            Level: session.Player.Value.Character.Level,
            Vip: session.Player.Value.Account.PremiumTime > DateTime.UtcNow.ToEpochSeconds(),
            Gm: GmCheck.IsGm(session.Player.Value.Account.AdminPermissions)));
    }

    // The flag for the player's current insignia. An id the server table does not hold is not shown.
    public static bool Shown(GameSession session) {
        return session.TableMetadata.InsigniaTable.Entries.TryGetValue(session.Player.Value.Character.Insignia, out InsigniaTable.Entry? entry)
               && Shown(session, entry);
    }
}

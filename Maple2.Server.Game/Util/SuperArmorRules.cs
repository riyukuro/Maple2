using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// Super armor against a hit's reaction, as the client tests it (AdditionalCliList::FUN_140970c10, @0x140970c10). The
// client runs the test for players only and leaves npc reactions to the server; the npc hit state (NpcControlRules)
// uses the same test here.
public static class SuperArmorRules {
    // SkillDamage::GetPush (@0x1407e0f90): pushApplyField 1 takes the push away on PvP (map type 9) and home (3)
    // maps, 2 everywhere else.
    public static bool PushApplies(int applyField, MapType mapType) {
        bool ground = mapType is MapType.Pvp or MapType.Home;
        return !(ground && applyField == 1) && !(!ground && applyField == 2);
    }

    // The attack's push on this map: null when it has none or the map takes it away.
    public static SkillMetadataPush? PushOn(SkillMetadataPush? push, MapType mapType) {
        return push is { Type: not 0 } && PushApplies(push.ApplyField, mapType) ? push : null;
    }

    // FUN_1409726e0 (@0x1409726e0): an armor counts with field type 0 anywhere, with 2 only on PvP maps, with any
    // other value only off them.
    public static bool ArmorCounts(int armor, int applyField, MapType mapType) {
        if (armor == 0) {
            return false;
        }
        return applyField == 0 || (applyField == 2) == (mapType == MapType.Pvp);
    }

    // The holder's armor: the larger of its cast's (CSkillRecord::GetSuperArmor, only while it casts) and its
    // effects' (each effect's that counts on the map, the largest).
    public static int Armor(SkillMetadataState? cast, IEnumerable<AdditionalEffectMetadataProperty> effects, MapType mapType) {
        int armor = cast != null && ArmorCounts((int) cast.SuperArmor, cast.SuperArmorApplyField, mapType) ? (int) cast.SuperArmor : 0;
        foreach (AdditionalEffectMetadataProperty effect in effects) {
            if (effect.SuperArmor > armor && ArmorCounts(effect.SuperArmor, effect.SuperArmorApplyField, mapType)) {
                armor = effect.SuperArmor;
            }
        }
        return armor;
    }

    // Whether the armor keeps the hit's reaction off. superArmorBreak always goes through; 1 gives way to a push or
    // a grab (grabTargetType 1), 2 to a push with pushUpDistance or a grab, 3 never. 4 gives way to a grab for the
    // hit reaction and to any push for the push itself (the client's second argument); the npc's hit state carries
    // its push, so a push or a grab moves it here.
    public static bool Holds(int armor, SkillMetadataAttack attack, SkillMetadataPush? push) {
        if (attack.Damage.SuperArmorBreak) {
            return false;
        }
        bool grab = attack.GrabTargetType == 1;
        return armor switch {
            1 => push == null && !grab,
            2 => (push == null || Math.Abs(push.UpDistance) < 0.0001f) && !grab,
            3 => true,
            4 => push == null && !grab,
            _ => false,
        };
    }
}

namespace Maple2.Server.Game.Util;

/// <summary>
/// Vibrate 0x26, the client's tag packet (WorldService::FUN_141bc3560, worldservice_1.cpp:9220): a skill's attack
/// area (a tag of its attack point) that meets a vibrate object's range sends one packet for it, then puts the
/// object in the tag's entered set, so a tag sends once per object. An object hidden by its group (AiVibrate) has no
/// collision on the client and meets no tag. The packet names the cast's skill uid, the tag's skill id and level and
/// its attack point.
/// </summary>
public static class VibrateRules {
    public enum Refusal {
        None,
        NoObject, // no vibrate object of the field has the id
        Hidden, // the object's group is hidden
        OtherSkill, // the skill id or level is not the cast's
        Spent, // the object already had one packet for each tag the attack point declared
    }

    // take: counts the packet against the cast's budget; called only when nothing else refuses it.
    public static Refusal Check(bool known, bool hidden, bool sameSkill, Func<bool> take) {
        if (!known) {
            return Refusal.NoObject;
        }
        if (hidden) {
            return Refusal.Hidden;
        }
        if (!sameSkill) {
            return Refusal.OtherSkill;
        }
        return take() ? Refusal.None : Refusal.Spent;
    }

    /// <summary>
    /// The Vibrate packets one cast has taken. Each firing of an attack point makes its tags and sends the attack
    /// point packet that declares them, before any of the tags is placed (StateSkill, stateskill.cpp:1348-1370; a tag
    /// joins the tag ranges only when its magic spawns, CMagic::FUN_141462490). A tag sends once per object: so an
    /// object takes at most as many packets as the attack point's taken packets declared attacks (SkillHitBudget),
    /// and none when they declared none.
    /// </summary>
    public sealed class Budget {
        private readonly Dictionary<(byte, string), int> taken = new();

        public bool Take(byte attackPoint, string entityId, int attacks) {
            int used = taken.GetValueOrDefault((attackPoint, entityId));
            if (used >= attacks) {
                return false;
            }
            taken[(attackPoint, entityId)] = used + 1;
            return true;
        }
    }
}

using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// The spirit and stamina orbs a player's hit on an npc may drop (special attributes NpcHitRewardSpBall and
// NpcHitRewardEpBall, 105 and 106 in the client's table: s_item_opt_sa_npc_hit_reward_sp_ball "Chance to Generate
// Spirit Orbs on Attack", _ep_ball for stamina). KR rolls them on gear: +0.4% to +1% on accessories, +5% to +50% on
// armor and weapons. The client only names them in tooltips and shows an orb (item 90000009 or 90000010) as it shows
// any drop (CDropItem::LoadSpepBallConstants). Chosen here, as nothing in the client or the tables says more:
// - each hit of the player on an npc rolls each attribute at its rate (0.05 = 5%);
// - an orb holds 20 points, the amount every SP and EP orb group of the server's global drop sets has;
// - after an orb of a kind drops, that kind waits 3000 ms, the period the server's orb drop box for field elites
//   and bosses (globalDropItemBox 100000) names.
public static class NpcHitRewardRules {
    public const int SpiritOrbItemId = 90000009;
    public const int StaminaOrbItemId = 90000010;
    public const int OrbAmount = 20;
    public const long CooldownMs = 3000;

    public static readonly (SpecialAttribute Attribute, int ItemId)[] Orbs = [
        (SpecialAttribute.NpcHitRewardSpBall, SpiritOrbItemId),
        (SpecialAttribute.NpcHitRewardEpBall, StaminaOrbItemId),
    ];

    // Whether a hit at now drops the orb: the kind's last orb (0 for none yet) is CooldownMs back or more, and roll (in
    // [0, 1)) is under the rate.
    public static bool Drops(long lastDropTick, long now, double rate, double roll) {
        if (rate <= 0) {
            return false;
        }
        if (lastDropTick != 0 && now - lastDropTick < CooldownMs) {
            return false;
        }
        return roll < rate;
    }
}

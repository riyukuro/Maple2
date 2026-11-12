using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Manager.Field;

// A trigger's PvP zone (set_pvp_zone) in one of the modes the server builds: the free-for-all (type 2,
// PvpZoneField), the Red Arena's face-offs (type 1, RedArenaZoneField) and Treasure Island's teams (type 3,
// TeamZoneField). The field gives it the hits, deaths, arrivals and departures, and its update.
public interface IPvpZoneField {
    int BoxId { get; }
    bool Ended { get; }
    bool Hostile(FieldPlayer caster, FieldPlayer target);
    bool IsParticipant(FieldPlayer player);
    void Update(long tick);
    void Damage(FieldPlayer attacker, FieldPlayer victim, long amount);
    void Death(FieldPlayer victim);
    void Enter(FieldPlayer player);
    void Leave(FieldPlayer player);
}

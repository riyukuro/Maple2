namespace Maple2.Model.Metadata;

// table/Server/npcStatFactorByPlayerCount.xml: <PlayerCountFactor factorID class playerCount hpRate hpValue papRate
// papValue mapRate mapValue nddRate nddValue capRate capValue>. A dungeon room's playerCountFactorID or an instance
// field's npcStatFactorID names a factorID; the row for the npc's class and the player count scales its stats.
// Entries: factorID -> npc class -> playerCount -> row.
public record NpcStatFactorTable(
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>> Entries) : ServerTable;

// Each stat becomes stat x rate + value (hp = Health, pap = PhysicalAtk, map = MagicalAtk, ndd = Defense,
// cap = CriticalRate).
public record NpcStatFactorMetadata(
    double HpRate,
    double HpValue,
    double PapRate,
    double PapValue,
    double MapRate,
    double MapValue,
    double NddRate,
    double NddValue,
    double CapRate,
    double CapValue);

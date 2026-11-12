namespace Maple2.Model.Metadata;

// table/Server/monsterExpTable.xml: <exp level common dungeonElite elite boss dungeonBoss>, the exp of a kill of a
// mob of that level by its rank. Its common column is the exp base table 2 (the table this server used for every mob).
// Entries: level -> row.
public record MonsterExpTable(IReadOnlyDictionary<int, MonsterExpMetadata> Entries) : ServerTable;

public record MonsterExpMetadata(
    long Common,
    long Elite,
    long Boss,
    long DungeonElite,
    long DungeonBoss) {
    // The npc's class (CNpcData class: 1 common, 3 elite, 4 boss, 6 dungeon boss, 7 dungeon elite; the classes of
    // npcStatFactorByPlayerCount). Any other class gets the common column.
    public long ForClass(int npcClass) => npcClass switch {
        3 => Elite,
        4 => Boss,
        6 => DungeonBoss,
        7 => DungeonElite,
        _ => Common,
    };
}

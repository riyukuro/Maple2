using System.Numerics;

namespace Maple2.Model.Metadata;

// table/birthdaytable.xml (client Xml.m2d, read only by the server): <birthday dayRange="a-b"><spawn fieldID><npc><v npcID
// spawnPointID/></npc><ugcBanner><v bannerID/></ugcBanner></spawn></birthday>. FirstDay/LastDay: dayRange, read as ISO
// weekdays (1 = Monday .. 7 = Sunday; Unknown, chosen: "1-7" is every day). Npcs: the npcs with the position and rotation
// of the field's event spawn point of that id (the field's xblock first, else a locale variant of it); npcs no xblock
// places are left out. Banners: the ugc banner ids, kept but not placed (no banner row or board for them).
public record BirthdayTable(int FirstDay, int LastDay, IReadOnlyDictionary<int, BirthdayTable.Field> Fields) : ServerTable {
    public record Field(IReadOnlyList<Npc> Npcs, IReadOnlyList<int> Banners);
    public record Npc(int NpcId, int SpawnPointId, Vector3 Position, Vector3 Rotation);

    // ISO weekday of the day, inside FirstDay..LastDay.
    public bool Active(DayOfWeek day) {
        int iso = day == DayOfWeek.Sunday ? 7 : (int) day;
        return iso >= FirstDay && iso <= LastDay;
    }
}

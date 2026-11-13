namespace Maple2.Model.Metadata;

// table/questmasknpc.xml as CQuestMaskTable::LoadFile reads it (commonfiles/questmasktable.cpp:317): per field, per
// npc id, the npc's mask. A field listed twice is one field; a key listed twice in a field keeps its last mask (SetAt).
public record QuestMaskNpcTable(IReadOnlyDictionary<int, IReadOnlyDictionary<int, QuestMaskNpcTable.Mask>> Fields) : Table {
    // Enable: enableInMask (1 when the row has none, QuestMask's default). Visible inside some range when 1, outside
    // every range when 0 (QuestMaskRules.Visible).
    public record Mask(bool Enable, IReadOnlyList<Range> Ranges);

    // A <mask> row: the start quest and its state mode, the end quest and its state mode (FUN_140764480's modes).
    public record Range(int StartQuestId, int StartState, int EndQuestId, int EndState);
}

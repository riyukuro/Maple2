using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Model.Metadata;

public record SmartPushTable(IReadOnlyDictionary<int, SmartPushMetadata> Entries) : Table;

public record SmartPushMetadata(
    int Id,
    string Content,
    SmartPushType Type,
    long Value,
    long MeretCost,
    IngredientInfo RequiredItem,
    // The item id of requireItem, used when it names no tag (CSmartPushTable, smartpushtable.cpp:138-141).
    int RequiredItemId = 0);

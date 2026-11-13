using Maple2.Model.Enum;
using Maple2.Model.Game.Shop;

namespace Maple2.Model.Metadata;

public record ShopItemTable(IReadOnlyDictionary<int, Dictionary<int, ShopItemMetadata>> Entries) : ServerTable;

public record ShopItemMetadata(
    int Id,
    int ShopId,
    int ItemId,
    byte Rarity,
    ShopCost Cost,
    int SellCount,
    string Category,
    ShopItemMetadata.Requirement Requirements,
    RestrictedBuyData? RestrictedBuyData,
    short SellUnit,
    ShopItemLabel SaleTag,
    string IconTag,
    bool WearForPreview,
    bool RandomOption,
    long Probability,
    bool IsPremiumItem,
    byte FrameType = 0) {

    public record Requirement(
        int GuildTrophy,
        Achievement Achievement,
        Championship Championship,
        GuildNpc GuildNpc,
        QuestAlliance QuestAlliance);

    public record Achievement(
        int Id,
        int Rank
    );

    public record Championship(
        byte Rank,
        short JoinCount);

    public record GuildNpc(
        GuildNpcType Type,
        short Level);

    // The alliance whose fame grade the product needs, in the client's fame alliance numbering (0 any alliance).
    public record QuestAlliance(
        AllianceType Type,
        int Grade);
}

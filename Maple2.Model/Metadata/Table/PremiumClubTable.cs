using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record PremiumClubTable(
    IReadOnlyDictionary<int, PremiumClubTable.Buff> Buffs,
    IReadOnlyDictionary<int, PremiumClubTable.Item> Items,
    IReadOnlyDictionary<int, PremiumClubTable.Package> Packages) : Table {

    public record Buff(
        int Id,
        short Level);

    public record Item(
        int Id,
        int Amount,
        int Rarity,
        int Period);

    // vipgoodstable.xml: the client bills SalePrice in PriceType's currency (UIVIPGoodsShopDialog::OnClickBuyGoods);
    // BuyLimit 0 is no limit, BuyLimitResetType is when the purchase count starts again (VipGoodsRules).
    public record Package(
        bool Disabled,
        long StartDate,
        long EndDate,
        long Period,
        long Price,
        long SalePrice,
        bool RedMeret,
        int BuyLimit,
        ResetType BuyLimitResetType,
        IList<Item> BonusItems);
}

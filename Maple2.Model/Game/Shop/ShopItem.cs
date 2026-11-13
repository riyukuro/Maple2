using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game.Shop;

public class ShopItem : IByteSerializable {
    public readonly ShopItemMetadata Metadata;
    public int Id => Metadata.Id;
    public int StockPurchased { get; set; }
    public int StockCount { get; set; }
    public required Item Item { get; init; }
    /// <summary>
    /// Items per purchase. Most shop rows leave sellUnit out (0); the client treats anything below 2 as 1
    /// (CNpcShopProductCli::SetBuyItemCount), so counts multiplied by it must too.
    /// </summary>
    public int SellUnit => Math.Max(1, (int) Metadata.SellUnit);

    public ShopItem(ShopItemMetadata metadata) {
        Metadata = metadata;
        StockCount = metadata.SellCount;
    }

    public ShopItem Clone() {
        return new ShopItem(Metadata) {
            Item = Item.Clone(),
            StockCount = StockCount,
            StockPurchased = StockPurchased,
        };
    }

    // The row's frame (UIShopDialog::SetProductBackground: 2 premium, 4 hot, 5-7 bf_1-3, else normal). The client's own
    // shop load (CNpcShopData, commonfiles/npcshopdata.cpp:1279) keeps the row's frameType and gives a row without one
    // 2 when it is a premiumItem, else 1.
    public static byte Frame(byte frameType, bool premiumItem) {
        return frameType != 0 ? frameType : (byte) (premiumItem ? 2 : 1);
    }

    public virtual void WriteTo(IByteWriter writer) {
        writer.WriteInt(Id);
        writer.WriteInt(Metadata.ItemId);
        writer.WriteClass<ShopCost>(Metadata.Cost);
        writer.WriteByte(Metadata.Rarity);
        writer.WriteInt(500);
        writer.WriteInt(StockCount);
        writer.WriteInt(StockPurchased * SellUnit);
        writer.WriteInt(Metadata.Requirements.GuildTrophy);
        writer.WriteString(Metadata.Category);
        writer.WriteInt(Metadata.Requirements.Achievement.Id);
        writer.WriteInt(Metadata.Requirements.Achievement.Rank);
        writer.WriteByte(Metadata.Requirements.Championship.Rank);
        writer.WriteShort(Metadata.Requirements.Championship.JoinCount);
        writer.WriteByte((byte) Metadata.Requirements.GuildNpc.Type);
        writer.WriteShort(Metadata.Requirements.GuildNpc.Level);
        writer.WriteBool(false);
        writer.WriteShort(Metadata.SellUnit);
        writer.WriteByte(Frame(Metadata.FrameType, Metadata.IsPremiumItem)); // CNpcShopProductCli m_bc, the row frame
        writer.WriteByte((byte) Metadata.SaleTag); // m_c0, the sale tag
        writer.WriteString(Metadata.IconTag);
        writer.WriteShort((short) Metadata.Requirements.QuestAlliance.Type);
        writer.WriteInt(Metadata.Requirements.QuestAlliance.Grade);
        writer.WriteBool(Metadata.WearForPreview);
        writer.WriteBool(Metadata.RestrictedBuyData != null);
        if (Metadata.RestrictedBuyData != null) {
            writer.WriteClass<Game.Shop.RestrictedBuyData>(Metadata.RestrictedBuyData);
        }

        writer.WriteBool(false); // KMS 2525: the client reads a has-NpcShopItemLife bool here (0x140db698c)
        writer.WriteClass<Item>(Item);
    }
}

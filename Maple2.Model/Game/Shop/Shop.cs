using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game.Shop;

public class Shop : IByteSerializable {
    public int Id => Metadata.Id;
    public readonly ShopMetadata Metadata;
    public ShopRestockData RestockData => Metadata.RestockData;
    public long RestockTime;
    public int RestockCount;
    public SortedDictionary<int, ShopItem> Items;

    public Shop(ShopMetadata metadata) {
        Metadata = metadata;
        RestockTime = metadata.RestockTime;
        Items = new SortedDictionary<int, ShopItem>();
    }

    // The next instant restock's cost, the one the shop packet shows (the client, UIShopDialog: m_29c and
    // m_nResetPriceOrg, sent back in Shop 9) and InstantRestock charges. Without the multiplier (the client's
    // useLuaResetPayment, read into the flag after the price) it is the table's price. With it the price is
    // the KMS2 server's Lua, which the client does not hold. Inferred: the cost climbs with the restocks made
    // since the reset, in the excess currency the client's reset tooltip shows (m_2a0), after five meso tiers.
    // Chosen here: the tier is the next restock's number (the count made plus one).
    public (ShopCurrencyType Type, int Price) RestockCost {
        get {
            if (!RestockData.EnablePriceMultiplier) {
                return (RestockData.CurrencyType, RestockData.Price);
            }
            return (RestockCount + 1) switch {
                <= 5 => (Constant.InitialTierExcessRestockCurrency, 50000),
                6 => (RestockData.ExcessCurrencyType, 10),
                7 => (RestockData.ExcessCurrencyType, 20),
                8 => (RestockData.ExcessCurrencyType, 40),
                9 => (RestockData.ExcessCurrencyType, 60),
                10 => (RestockData.ExcessCurrencyType, 80),
                <= 15 => (RestockData.ExcessCurrencyType, 100),
                _ => (RestockData.ExcessCurrencyType, 150),
            };
        }
    }

    public virtual void WriteTo(IByteWriter writer) {
        writer.WriteInt(Id);
        writer.WriteLong(RestockTime);
        writer.WriteInt();
        writer.WriteShort((short) Items.Count);
        writer.WriteInt(Metadata.CategoryId);
        writer.WriteBool(Metadata.OpenWallet);
        writer.WriteBool(Metadata.IsOnlySell);
        writer.WriteBool(Metadata.EnableReset);
        writer.WriteBool(Metadata.DisableDisplayOrderSort);
        writer.Write<ShopFrameType>(Metadata.FrameType);
        writer.WriteBool(Metadata.DisplayOnlyUsable);
        writer.WriteBool(Metadata.HideStats);
        writer.WriteBool(Metadata.DisplayProbability);
        writer.WriteBool(Metadata.DisplayNew);
        writer.WriteString(Metadata.Name);
        if (Metadata.EnableReset) {
            (ShopCurrencyType restockType, int restockPrice) = RestockCost;
            writer.Write<ShopCurrencyType>(restockType);
            writer.Write<ShopCurrencyType>(RestockData.ExcessCurrencyType);
            writer.WriteInt();
            writer.WriteInt(restockPrice);
            writer.WriteBool(RestockData.EnablePriceMultiplier);
            writer.WriteInt(RestockCount);
            writer.Write<ResetType>(RestockData.ResetType);
            writer.WriteBool(RestockData.DisableInstantRestock);
            writer.WriteBool(RestockData.AccountWide);
        }
    }
}

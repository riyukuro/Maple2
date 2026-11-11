using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

public sealed class ItemEnchant : IByteSerializable, IByteDeserializable {
    public static readonly ItemEnchant Default = new ItemEnchant();

    public int Enchants { get; set; }
    public int EnchantExp { get; set; }
    // Enchant based peachy charges, otherwise always require 10 charges
    public byte EnchantCharges { get; set; }
    public int Charges { get; set; }

    public readonly Dictionary<BasicAttribute, BasicOption> BasicOptions;

    public ItemEnchant(int enchants = 0, int enchantExp = 0, byte enchantCharges = 1, int charges = 0,
                       Dictionary<BasicAttribute, BasicOption>? basicOptions = null) {
        Enchants = enchants;
        EnchantExp = enchantExp;
        EnchantCharges = enchantCharges;
        Charges = charges;
        BasicOptions = basicOptions ?? new Dictionary<BasicAttribute, BasicOption>();
    }

    public ItemEnchant Clone() {
        return new ItemEnchant(Enchants, EnchantExp, EnchantCharges, Charges, new Dictionary<BasicAttribute, BasicOption>(BasicOptions));
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(Enchants);
        writer.WriteInt(EnchantExp);
        writer.WriteByte(EnchantCharges);

        // Unk_EnchantHistory::Read (itemenchantinfo.cpp:28): i64, i32, i32, bool, i32
        writer.WriteLong();
        writer.WriteInt();
        writer.WriteInt();
        // The bool is not "tradeable". The client's own default is true (Reset, itemenchantinfo.cpp:17), and
        // while it is false the client refuses, whatever the transfer flags say, to destroy the item
        // (itemsocketsystemutil.cpp:2316), to trade it (:2440), to list it on the black market (:2468) and to
        // repack it (itemrepackingscrolldata.cpp:154), and hides the repack count line of the tooltip
        // (astooltip.cpp:3524). The server has no state that locks an item this way, so it is always true.
        writer.WriteBool(true);
        writer.WriteInt(Charges);

        writer.WriteByte((byte) BasicOptions.Count);
        foreach ((BasicAttribute type, BasicOption option) in BasicOptions) {
            writer.WriteInt((int) type);
            writer.Write<BasicOption>(option);
        }
    }

    public void ReadFrom(IByteReader reader) {
        Enchants = reader.ReadInt();
        EnchantExp = reader.ReadInt();
        EnchantCharges = reader.ReadByte();
        reader.ReadLong();
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadBool();
        Charges = reader.ReadInt();

        byte count = reader.ReadByte();
        for (int i = 0; i < count; i++) {
            var type = (BasicAttribute) reader.ReadInt();
            BasicOptions[type] = reader.Read<BasicOption>();
        }
    }
}

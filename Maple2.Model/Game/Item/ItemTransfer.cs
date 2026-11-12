using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

public sealed class ItemTransfer : IByteSerializable, IByteDeserializable {
    public static readonly ItemTransfer Default = new ItemTransfer();

    public TransferFlag Flag { get; set; }
    public int RemainTrades { get; set; }
    public int RepackageCount { get; set; }

    public ItemBinding? Binding { get; private set; }

    public ItemTransfer(TransferFlag flag = 0, int remainTrades = 0, int repackageCount = 0, ItemBinding? binding = null) {
        Flag = flag;
        RemainTrades = remainTrades;
        RepackageCount = repackageCount;
        Binding = binding;
    }

    public ItemTransfer Clone() {
        return new ItemTransfer(Flag, RemainTrades, RepackageCount, Binding?.Clone());
    }

    public bool Bind(Character character) {
        if (!Flag.HasFlag(TransferFlag.Bind)) {
            return false;
        }

        if (Binding != null) {
            return false;
        }

        Binding = new ItemBinding(character.Id, character.Name);
        RemainTrades = 0;
        return true;
    }

    public void WriteTo(IByteWriter writer) {
        // CItemTransfer is CItem[66]
        // The client's own rule (CItemTransfer::ComputeRule) only knows Trade (4), Bind (8) and LimitTrade (16), and
        // KMS2 sends a tradable item as 4: Split is a server-side rule and goes out as 0.
        writer.WriteInt((int) (Flag & ~TransferFlag.Split)); // CItemTransfer[5]
        writer.WriteBool(false); // CItemTransfer[9] *bit-1*
        writer.WriteInt(RemainTrades); // CItemTransfer[10]
        writer.WriteInt(RepackageCount); // CItemTransfer[11]
        writer.WriteBool(false); // CItemTransfer[12]
        writer.WriteBool(true); // CItemTransfer[9] *bit-10* (socketTransfer?)

        // CharBound means untradable, unsellable, bound to char (ignores TransferFlag)
        writer.WriteBool(Binding != null);
        if (Binding != null) {
            writer.WriteClass<ItemBinding>(Binding);
        }
    }

    public void ReadFrom(IByteReader reader) {
        Flag = reader.Read<TransferFlag>();
        reader.ReadByte();
        RemainTrades = reader.ReadInt();
        RepackageCount = reader.ReadInt();
        reader.ReadBool();
        reader.ReadBool();
        bool isBound = reader.ReadBool();
        if (isBound) {
            Binding = reader.ReadClass<ItemBinding>();
        }
    }

    public override bool Equals(object? obj) {
        if (ReferenceEquals(this, obj)) return true;
        if (!(obj is ItemTransfer other)) return false;
        return Flag == other.Flag && RemainTrades == other.RemainTrades && RepackageCount == other.RepackageCount
               && Equals(Binding, other.Binding);
    }

    public override int GetHashCode() {
        return HashCode.Combine((int) Flag, RemainTrades, RepackageCount, Binding);
    }
}

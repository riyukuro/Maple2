using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

public sealed class ItemSocket : IByteSerializable, IByteDeserializable {
    public static readonly ItemSocket Default = new ItemSocket(0, 0);

    public byte MaxSlots;
    public byte UnlockSlots {
        get => (byte) Sockets.Length;
        set => Array.Resize(ref Sockets, Math.Min(MaxSlots, value));
    }

    public ItemGemstone?[] Sockets;

    public ItemSocket(byte maxSlots, byte unlocked) {
        MaxSlots = maxSlots;
        Sockets = new ItemGemstone?[unlocked];
    }

    public ItemSocket(byte maxSlots, ItemGemstone?[] sockets) {
        MaxSlots = maxSlots;
        Sockets = sockets;
    }

    public ItemSocket Clone() {
        var sockets = new ItemGemstone?[Sockets.Length];
        for (int i = 0; i < Sockets.Length; i++) {
            sockets[i] = Sockets[i]?.Clone();
        }

        return new ItemSocket(MaxSlots, sockets);
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteByte(MaxSlots);
        writer.WriteByte(UnlockSlots);
        for (int i = 0; i < UnlockSlots; i++) {
            ItemGemstone? gem = Sockets[i];
            writer.WriteBool(gem != null);
            if (gem != null) {
                writer.WriteClass<ItemGemstone>(gem);
            }
        }
    }

    public void ReadFrom(IByteReader reader) {
        MaxSlots = reader.ReadByte();
        UnlockSlots = reader.ReadByte();
        for (int i = 0; i < UnlockSlots; i++) {
            bool hasGem = reader.ReadBool();
            if (hasGem) {
                Sockets[i] = reader.ReadClass<ItemGemstone>();
            }
        }
    }
}

public class ItemGemstone : IByteSerializable, IByteDeserializable {
    public int ItemId;
    public ItemBinding? Binding;
    public ItemStats? Stats;
    public bool IsLocked;
    public long UnlockTime;
    // Server only, not written: the bag gem's transfer (trade count, flag, binding) kept while it sits in the socket,
    // so the gem taken out again is the gem that went in. Null on a gem socketed before it was kept.
    public ItemTransfer? Transfer;

    public ItemGemstone(int itemId = 0, ItemBinding? binding = null, ItemStats? stats = null, bool isLocked = false, long unlockTime = 0,
        ItemTransfer? transfer = null) {
        if (stats == null) throw new ArgumentNullException(nameof(stats));
        ItemId = itemId;
        Binding = binding;
        IsLocked = isLocked;
        UnlockTime = unlockTime;
        Stats = stats;
        Transfer = transfer;
    }

    public ItemGemstone Clone() {
        return new ItemGemstone(ItemId, Binding?.Clone(), Stats?.Clone(), IsLocked, UnlockTime, Transfer?.Clone());
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(ItemId);

        writer.WriteBool(Binding != null);
        if (Binding != null) {
            writer.WriteClass<ItemBinding>(Binding);
        }

        WriteLock(writer, IsLocked, UnlockTime);
    }

    // The gem's lock record as the client builds it from a bag gem (TSocketGemStoneInfo::SetLockFromItem and
    // TrySetLock, itemsocket.cpp:184-205): state 1 = locked, state 0 = unlocking until the expire time. A gem neither
    // locked nor still unlocking has no record. The client reads state 0 as locked only while the server time is
    // before the expire time (TSocketGemStoneLock::IsLocked, itemsocket.cpp:58), so a locked gem must send state 1:
    // its UnlockTime is the time it was locked.
    public static void WriteLock(IByteWriter writer, bool isLocked, long unlockTime) {
        bool unlocking = !isLocked && unlockTime > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.WriteBool(isLocked || unlocking);
        if (isLocked || unlocking) {
            writer.WriteByte((byte) (isLocked ? 1 : 0));
            writer.WriteLong(unlockTime);
        }
    }

    public void ReadFrom(IByteReader reader) {
        ItemId = reader.ReadInt();

        bool isBound = reader.ReadBool();
        if (isBound) {
            Binding = reader.ReadClass<ItemBinding>();
        }

        IsLocked = false;
        UnlockTime = 0;
        if (reader.ReadBool()) {
            IsLocked = reader.ReadByte() == 1;
            UnlockTime = reader.ReadLong();
        }
    }
}

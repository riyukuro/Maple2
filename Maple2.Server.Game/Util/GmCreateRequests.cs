using System.Numerics;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Game.Util;

// The bodies of the three packets the client's GM create windows send, read as the client writes them.

// 0x15 RequestItemCreate (item create window, "OnCreateItem"; CCCreateItemPacket).
public readonly record struct GmItemCreateRequest(int ItemId, int Amount, byte Rarity, int Period, int Level, int Enchant, int OptionCount) {
    private const int OptionSets = 5;

    public static GmItemCreateRequest Read(IByteReader packet) {
        int itemId = packet.ReadInt();
        int amount = packet.ReadInt();
        byte rarity = packet.ReadByte();
        int period = packet.ReadInt();
        int level = packet.ReadInt();
        int enchant = packet.ReadInt(); // level - 15 when the level is over 15, capped by the item
        packet.ReadByte(); // 1 when the next byte is set
        packet.ReadByte();
        packet.ReadInt(); // -1 from the plain window
        packet.ReadInt(); // -1 from the plain window
        packet.ReadInt();

        // Five option sets, two key/value maps each. The plain window sends them empty; the option
        // window fills them.
        int options = 0;
        for (int i = 0; i < OptionSets * 2; i++) {
            byte count = packet.ReadByte();
            for (int j = 0; j < count; j++) {
                packet.ReadInt(); // key
                packet.ReadInt(); // value
            }
            options += count;
        }

        return new GmItemCreateRequest(itemId, amount, rarity, period, level, enchant, options);
    }
}

// 0x24 RequestNpcCreate (NPC create window). The pet button sets the flag and adds one float.
public readonly record struct GmNpcCreateRequest(int NpcId, Vector3 Position, Vector3 Rotation, int Count, bool Pet, float PetValue) {
    public static GmNpcCreateRequest Read(IByteReader packet) {
        int npcId = packet.ReadInt();
        var position = packet.Read<Vector3>();
        var rotation = packet.Read<Vector3>();
        int count = packet.ReadInt();
        bool pet = packet.ReadBool();
        float petValue = pet ? packet.ReadFloat() : 0f;

        return new GmNpcCreateRequest(npcId, position, rotation, count, pet, petValue);
    }
}

// 0x5C RequestAdditionalEffect (effect control window). Mode 0 adds an effect to the sender,
// mode 1 is the reset button and has no body. The packet names no target.
public readonly record struct GmEffectRequest(byte Mode, int EffectId, short Level, float Value) {
    public const byte Add = 0;
    public const byte Reset = 1;

    public static GmEffectRequest Read(IByteReader packet) {
        byte mode = packet.ReadByte();
        if (mode != Add) {
            return new GmEffectRequest(mode, 0, 0, 0f);
        }

        int effectId = packet.ReadInt();
        int level = packet.ReadInt(); // a 16-bit level, written as an int
        float value = packet.ReadFloat();
        return new GmEffectRequest(mode, effectId, (short) level, value);
    }
}

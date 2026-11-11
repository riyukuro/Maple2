using System.Numerics;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Game;

// gosCoupleEmotion (state 58). Client: StateSyncObjectCoupleEmotion (commonfiles/statesyncobject.cpp:290-316):
// after the base record int, int, byte, vector, vector.
public class StateSyncCoupleEmotion : StateSync {
    public int UnknownCoupleEmotionInt1;
    public int UnknownCoupleEmotionInt2;
    public byte UnknownCoupleEmotionByte;
    public Vector3 UnknownCoupleEmotionVector1;
    public Vector3 UnknownCoupleEmotionVector2;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteInt(UnknownCoupleEmotionInt1);
        writer.WriteInt(UnknownCoupleEmotionInt2);
        writer.WriteByte(UnknownCoupleEmotionByte);
        writer.WriteFloat(UnknownCoupleEmotionVector1.X);
        writer.WriteFloat(UnknownCoupleEmotionVector1.Y);
        writer.WriteFloat(UnknownCoupleEmotionVector1.Z);
        writer.WriteFloat(UnknownCoupleEmotionVector2.X);
        writer.WriteFloat(UnknownCoupleEmotionVector2.Y);
        writer.WriteFloat(UnknownCoupleEmotionVector2.Z);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        UnknownCoupleEmotionInt1 = reader.ReadInt();
        UnknownCoupleEmotionInt2 = reader.ReadInt();
        UnknownCoupleEmotionByte = reader.ReadByte();
        UnknownCoupleEmotionVector1 = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        UnknownCoupleEmotionVector2 = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
    }
}

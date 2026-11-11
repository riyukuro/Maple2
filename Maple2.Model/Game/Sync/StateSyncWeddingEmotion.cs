using Maple2.PacketLib.Tools;

namespace Maple2.Model.Game;

// gosWeddingEmotion. Client: StateSyncObjectWeddingEmotion (commonfiles/statesyncobject.cpp:346-371):
// bool 1, bool 2; the int follows when bool 2 is set, the int and bool pair when bool 1 is set.
public class StateSyncWeddingEmotion : StateSync {
    public bool UnknownWeddingEmotionBool1;
    public bool UnknownWeddingEmotionBool2;
    public int UnknownWeddingEmotionInt1;
    public int UnknownWeddingEmotionInt2;
    public bool UnknownWeddingEmotionBool3;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteBool(UnknownWeddingEmotionBool1);
        writer.WriteBool(UnknownWeddingEmotionBool2);
        if (UnknownWeddingEmotionBool2) {
            writer.WriteInt(UnknownWeddingEmotionInt1);
        }
        if (UnknownWeddingEmotionBool1) {
            writer.WriteInt(UnknownWeddingEmotionInt2);
            writer.WriteBool(UnknownWeddingEmotionBool3);
        }
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        UnknownWeddingEmotionBool1 = reader.ReadBool();
        UnknownWeddingEmotionBool2 = reader.ReadBool();
        if (UnknownWeddingEmotionBool2) {
            UnknownWeddingEmotionInt1 = reader.ReadInt();
        }
        if (UnknownWeddingEmotionBool1) {
            UnknownWeddingEmotionInt2 = reader.ReadInt();
            UnknownWeddingEmotionBool3 = reader.ReadBool();
        }
    }
}
